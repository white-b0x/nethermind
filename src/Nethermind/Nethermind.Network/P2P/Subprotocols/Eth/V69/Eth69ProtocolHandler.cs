// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Consensus;
using Nethermind.Consensus.Scheduler;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Network.Contract.P2P;
using Nethermind.Network.P2P.ProtocolHandlers;
using Nethermind.Network.P2P.EventArg;
using Nethermind.Network.P2P.Subprotocols.Eth.V66.Messages;
using Nethermind.Network.P2P.Subprotocols.Eth.V68;
using Nethermind.Network.P2P.Subprotocols.Eth.V69.Messages;
using Nethermind.Network.Rlpx;
using Nethermind.Stats;
using Nethermind.Stats.Model;
using Nethermind.Synchronization;
using Nethermind.TxPool;

namespace Nethermind.Network.P2P.Subprotocols.Eth.V69;

/// <summary>
/// https://eips.ethereum.org/EIPS/eip-7642
/// </summary>
public class Eth69ProtocolHandler(
    ISession session,
    IMessageSerializationService serializer,
    INodeStatsManager nodeStatsManager,
    ISyncServer syncServer,
    IBackgroundTaskScheduler backgroundTaskScheduler,
    ITxPool txPool,
    IGossipPolicy gossipPolicy,
    IForkInfo forkInfo,
    ILogManager logManager,
    ITxPoolConfig txPoolConfig,
    ISpecProvider specProvider,
    ITxGossipPolicy? transactionsGossipPolicy = null)
    : Eth68ProtocolHandler(session, serializer, nodeStatsManager, syncServer, backgroundTaskScheduler, txPool,
        gossipPolicy, forkInfo, logManager, txPoolConfig, specProvider, transactionsGossipPolicy), ISyncPeer, IStaticProtocolInfo
{
    public override string Name => "eth69";

    public new static byte Version => EthVersions.Eth69;
    public override byte ProtocolVersion => Version;

    public override int MessageIdSpaceSize => 18;

    // ETH69 omits totalDifficulty in STATUS and BlockRangeUpdate messages. For PoW chains
    // (Ethash / Etchash) we resolve it via 3-tier lookup so the sync manager can prioritise peers.
    private UInt256? _resolvedTd;
    private readonly bool _isPoWChain =
        specProvider.SealEngine is SealEngineType.Ethash or SealEngineType.Etchash;

    public override UInt256? TotalDifficulty
    {
        get => _resolvedTd;
        set => _resolvedTd = value;
    }

    protected override bool HandleMessageCore(ZeroPacket message)
    {
        int size = message.Content.ReadableBytes;
        switch (message.PacketType)
        {
            case Eth69MessageCode.Status:
                StatusMessage69 statusMsg = Deserialize<StatusMessage69>(message.Content);
                ReportIn(statusMsg, size);
                Handle(statusMsg);
                return true;
            case Eth69MessageCode.Receipts:
                ReceiptsMessage69 receiptsMessage = Deserialize<ReceiptsMessage69>(message.Content);
                ReportIn(receiptsMessage, size);
                base.Handle(receiptsMessage, size);
                return true;
            case Eth69MessageCode.GetReceipts:
                HandleInBackground<GetReceiptsMessage, ReceiptsMessage69>(message, Handle);
                return true;
            case Eth69MessageCode.BlockRangeUpdate:
                BlockRangeUpdateMessage blockRangeUpdateMsg = Deserialize<BlockRangeUpdateMessage>(message.Content);
                ReportIn(blockRangeUpdateMsg, size);
                Handle(blockRangeUpdateMsg);
                return true;
            default:
                return base.HandleMessageCore(message);
        }
    }

    private void Handle(StatusMessage69 status)
    {
        if (_statusReceived)
        {
            throw new SubprotocolException("StatusMessage has already been received in the past");
        }

        _statusReceived = true;
        _remoteHeadBlockHash = status.LatestBlockHash;

        ReceivedProtocolInitMsg(status);

        SyncPeerProtocolInitializedEventArgs eventArgs = new(this)
        {
            NetworkId = (ulong)status.NetworkId,
            BestHash = status.LatestBlockHash,
            GenesisHash = status.GenesisHash,
            Protocol = status.Protocol,
            ProtocolVersion = status.ProtocolVersion,
            ForkId = status.ForkId
        };

        Session.IsNetworkIdMatched = SyncServer.NetworkId == (ulong)status.NetworkId;
        HeadNumber = status.LatestBlock;
        HeadHash = status.LatestBlockHash;

        if (_isPoWChain)
        {
            _resolvedTd = ResolvePowTd(status.LatestBlockHash, status.LatestBlock, SyncServer);
            if (Logger.IsDebug)
                Logger.Debug($"ETH69 PoW TD resolved: td={_resolvedTd} latestBlock={status.LatestBlock} peer={Node:c}");
        }

        NotifyProtocolInitialized(eventArgs);
    }

    private void Handle(BlockRangeUpdateMessage blockRangeUpdate)
    {
        if (blockRangeUpdate.EarliestBlock > blockRangeUpdate.LatestBlock)
        {
            Disconnect(
                DisconnectReason.InvalidBlockRangeUpdate,
                $"BlockRangeUpdate with earliest ({blockRangeUpdate.EarliestBlock}) > latest ({blockRangeUpdate.LatestBlock})."
            );
        }
        else if (blockRangeUpdate.LatestBlockHash.IsZero)
        {
            Disconnect(
                DisconnectReason.InvalidBlockRangeUpdate,
                "BlockRangeUpdate with latest block hash as zero."
            );
        }
        else
        {
            _remoteHeadBlockHash = blockRangeUpdate.LatestBlockHash;
            HeadNumber = blockRangeUpdate.LatestBlock;
            HeadHash = blockRangeUpdate.LatestBlockHash;

            if (_isPoWChain)
            {
                UInt256? refreshed = ResolvePowTd(blockRangeUpdate.LatestBlockHash, blockRangeUpdate.LatestBlock, SyncServer);
                if (refreshed > _resolvedTd)
                {
                    _resolvedTd = refreshed;
                    if (Logger.IsDebug)
                        Logger.Debug($"ETH69 PoW TD refreshed: td={_resolvedTd} latestBlock={blockRangeUpdate.LatestBlock} peer={Node:c}");
                }
            }
        }
    }

    /// <remarks>
    /// ETH69 STATUS and BlockRangeUpdate omit totalDifficulty. For PoW chains we resolve it via
    /// 3-tier: Tier 1 exact hash lookup (succeeds when peer's block is already in our chain),
    /// Tier 2 canonical-number lookup (accurate for any peer at or below our head height),
    /// Tier 3 rolling-window estimate: mean block difficulty over the last 10,000 blocks × gap.
    /// Avoids two failure modes: (1) all-time average is contaminated by ETC's pre-merge
    /// low-hashrate era; (2) point-in-time headDiff rides 25-35% weekly hashrate swings and
    /// overestimates during peaks. The 10K window (~36 hours) stays within the current difficulty
    /// regime and transitions naturally through the merge boundary during initial sync. Falls back
    /// to genesis difficulty at block 0. Returns null only when genesis has not yet been imported.
    /// </remarks>
    private static UInt256? ResolvePowTd(Hash256 bestHash, long bestNumber, ISyncServer syncServer)
    {
        // Tier 1: exact hash — succeeds when peer's block is already in our chain
        UInt256? td = syncServer.FindHeader(bestHash)?.TotalDifficulty;
        if (td is not null) return td;

        // Tier 2: canonical block-number lookup — accurate for any peer ≤ our head height
        Hash256? canonicalHash = syncServer.FindHash(bestNumber);
        if (canonicalHash is not null)
        {
            td = syncServer.FindHeader(canonicalHash)?.TotalDifficulty;
            if (td is not null) return td;
        }

        // Tier 3: rolling-window estimate — fallback when peer is ahead of our chain head.
        // COLD_START guard: return null only when genesis is not yet imported.
        BlockHeader? head = syncServer.Head;
        if (head is null) return null;

        UInt256 ourTd = head.TotalDifficulty ?? UInt256.Zero;
        long gap = Math.Max(0L, bestNumber - head.Number);
        UInt256 rate = RollingWindowDiff(head, ourTd, syncServer);
        return ourTd + rate * (UInt256)(ulong)gap;
    }

    private const long Tier3RollingWindow = 10_000L;

    private static UInt256 RollingWindowDiff(BlockHeader head, UInt256 headTd, ISyncServer syncServer)
    {
        // genesis: use genesis difficulty as rate (genesis TD is real PoW data, ~17B on ETC)
        if (head.Number == 0) return head.Difficulty;
        // insufficient history: all-available mean
        if (head.Number < Tier3RollingWindow)
            return headTd / (UInt256)(ulong)head.Number;
        // rolling window: mean diff over last 10K blocks — stays in current difficulty regime
        Hash256? windowHash = syncServer.FindHash(head.Number - Tier3RollingWindow);
        BlockHeader? windowHeader = windowHash is null ? null : syncServer.FindHeader(windowHash);
        if (windowHeader?.TotalDifficulty is null) return head.Difficulty; // window block evicted: fallback
        return (headTd - windowHeader.TotalDifficulty.Value) / (UInt256)Tier3RollingWindow;
    }

    private new async Task<ReceiptsMessage69> Handle(GetReceiptsMessage getReceiptsMessage, CancellationToken cancellationToken)
    {
        ReceiptsMessage message = await base.Handle(getReceiptsMessage, cancellationToken);
        return new(message.RequestId, message.EthMessage);
    }

    protected override void NotifyOfStatus(BlockHeader head)
    {
        StatusMessage69 statusMessage = new()
        {
            ProtocolVersion = ProtocolVersion,
            NetworkId = SyncServer.NetworkId,
            GenesisHash = SyncServer.Genesis.Hash!,
            ForkId = _forkInfo.GetForkId(head.Number, head.Timestamp),
            EarliestBlock = SyncServer.LowestBlock,
            LatestBlock = head.Number,
            LatestBlockHash = head.Hash!
        };

        Send(statusMessage);
    }

    public void NotifyOfNewRange(BlockHeader earliest, BlockHeader latest)
    {
        if (earliest.Number > latest.Number)
            throw new ArgumentException($"Earliest block ({earliest.Number}) greater than latest ({latest.Number}) in BlockRangeUpdate.");

        if (latest.Hash is null || latest.Hash.IsZero)
            throw new ArgumentException($"Latest block ({latest.Number}) hash is not provided.");

        if (Logger.IsTrace)
            Logger.Trace($"OUT {Counter:D5} BlockRangeUpdate to {Node:c}");

        BlockRangeUpdateMessage msg = new()
        {
            EarliestBlock = earliest.Number,
            LatestBlock = latest.Number,
            LatestBlockHash = latest.Hash
        };

        Send(msg);
    }
}

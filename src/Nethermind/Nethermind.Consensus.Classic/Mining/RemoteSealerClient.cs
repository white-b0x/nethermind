// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Consensus.Classic.Ethash;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;

[assembly: InternalsVisibleTo("Nethermind.Consensus.Classic.Test")]

namespace Nethermind.Consensus.Classic.Mining;

internal sealed class RemoteSealerClient(IEthash ethash, long ecip1099Transition, ILogManager logManager) : IRemoteSealerClient
{
    private const int MaxRecentWorkItems = 8;

    private readonly IEthash _ethash = ethash;
    private readonly long _ecip1099Transition = ecip1099Transition;
    private readonly uint _transitionEpoch = (uint)(ecip1099Transition / EtchashMiningHelper.EpochLength);
    private readonly ILogger _logger = logManager.GetClassLogger<RemoteSealerClient>();
    private readonly ConcurrentDictionary<Hash256, Block> _recentWork = new();
    private readonly object _lock = new();

    private Block? _currentBlock;
    private MiningWork? _currentWork;
    private Action<Block>? _onBlockMined;

    public MiningWork? GetWork()
    {
        lock (_lock)
        {
            return _currentWork;
        }
    }

    public bool SubmitWork(PoWSolution solution)
    {
        if (!_recentWork.TryGetValue(solution.PowHash, out Block? block))
        {
            if (_logger.IsDebug) _logger.Debug($"SubmitWork: unknown pow-hash {solution.PowHash}");
            return false;
        }

        block.Header.Nonce = solution.Nonce;
        block.Header.MixHash = solution.MixHash;

        if (!_ethash.Validate(block.Header))
        {
            if (_logger.IsDebug) _logger.Debug($"SubmitWork: invalid solution for block {block.Number}");
            return false;
        }

        block.Header.Hash = block.Header.CalculateHash();

        if (_logger.IsInfo) _logger.Info($"SubmitWork: valid solution for block {block.Number}, hash={block.Hash}");

        _recentWork.TryRemove(solution.PowHash, out _);

        lock (_lock)
        {
            if (_currentWork?.PowHash == solution.PowHash)
            {
                _currentWork = null;
                _currentBlock = null;
            }
        }

        _onBlockMined?.Invoke(block);

        return true;
    }

    public void SubmitNewWork(Block block)
    {
        Hash256 powHash = ComputePowHash(block.Header);
        Hash256 seedHash = ComputeSeedHash(block.Number);
        Hash256 target = ComputeTarget(block.Header.Difficulty);

        MiningWork work = new(powHash, seedHash, target, block.Number);

        lock (_lock)
        {
            _currentBlock = block;
            _currentWork = work;
        }

        _recentWork[powHash] = block;
        CleanupOldWork();

        if (_logger.IsDebug) _logger.Debug($"SubmitNewWork: block {block.Number}, powHash={powHash}, target={target}");
    }

    public void SetOnBlockMined(Action<Block> callback) => _onBlockMined = callback;

    private static Hash256 ComputePowHash(BlockHeader header)
    {
        byte[] encoded = new HeaderDecoder().Encode(header, RlpBehaviors.ForSealing).Bytes;
        return Keccak.Compute(encoded);
    }

    private Hash256 ComputeSeedHash(long blockNumber)
    {
        uint dagEpoch = GetEtchashEpoch(blockNumber);
        bool ecip1099Active = blockNumber >= _ecip1099Transition;
        uint seedEpoch = EtchashMiningHelper.GetSeedEpoch(dagEpoch, ecip1099Active);
        return EthashBase.GetSeedHash(seedEpoch);
    }

    private static Hash256 ComputeTarget(in Nethermind.Int256.UInt256 difficulty)
    {
        if (difficulty.IsZero)
            return Hash256.Zero;

        return new Hash256(EtchashMiningHelper.ComputeTargetBytes((BigInteger)difficulty));
    }

    private uint GetEtchashEpoch(long blockNumber) =>
        EtchashMiningHelper.GetEtchashEpoch(blockNumber, _ecip1099Transition, _transitionEpoch);

    private void CleanupOldWork()
    {
        while (_recentWork.Count > MaxRecentWorkItems)
        {
            foreach ((Hash256 key, _) in _recentWork)
            {
                if (_recentWork.TryRemove(key, out _))
                    break;
            }
        }
    }
}

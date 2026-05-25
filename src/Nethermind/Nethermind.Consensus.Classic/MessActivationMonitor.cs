// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Synchronization.Peers;

namespace Nethermind.Consensus.Classic;

internal sealed class MessActivationMonitor(
    EtcBlockTree blockTree,
    ISyncPeerPool syncPeerPool,
    ITimestamper timestamper,
    ILogManager logManager) : IDisposable
{
    private const int CheckIntervalMs = 390_000;
    private const int MinPeers = 5;
    private const ulong MaxHeadAgeSec = 390;

    private readonly EtcBlockTree _blockTree = blockTree;
    private readonly ISyncPeerPool _syncPeerPool = syncPeerPool;
    private readonly ITimestamper _timestamper = timestamper;
    private readonly ILogger _logger = logManager.GetClassLogger<MessActivationMonitor>();
    private Timer? _timer;

    public void Start()
    {
        _timer = new Timer(_ => Check(), null, CheckIntervalMs, CheckIntervalMs);
        if (_logger.IsInfo) _logger.Info("MESS activation monitor started");
    }

    private void Check()
    {
        bool wasPreviouslyEnabled = _blockTree.IsMessEnabled;

        int peerCount = _syncPeerPool.PeerCount;
        bool enoughPeers = peerCount >= MinPeers;

        BlockHeader? head = _blockTree.Head?.Header;
        bool headFresh = false;
        if (head is not null)
        {
            ulong now = _timestamper.UnixTime.Seconds;
            headFresh = now - head.Timestamp < MaxHeadAgeSec;
        }

        bool shouldEnable = enoughPeers && headFresh;
        _blockTree.EnableMess(shouldEnable);

        if (shouldEnable != wasPreviouslyEnabled)
        {
            if (_logger.IsInfo) _logger.Info(
                $"MESS {(shouldEnable ? "activated" : "deactivated")}: " +
                $"peers={peerCount}, headFresh={headFresh}");
        }
    }

    public void Dispose() => _timer?.Dispose();
}

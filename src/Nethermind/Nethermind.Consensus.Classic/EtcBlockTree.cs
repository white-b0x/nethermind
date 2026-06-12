// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Autofac.Features.AttributeFilters;
using Nethermind.Blockchain;
using Nethermind.Blockchain.BlockAccessLists;
using Nethermind.Blockchain.Blocks;
using Nethermind.Blockchain.Headers;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Db;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.State.Repositories;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Overrides HeadImprovementRequirementsSatisfied to apply MESS (ECBP-1100) antigravity scoring on reorg attempts.
/// </summary>
internal class EtcBlockTree(
    IBlockStore? blockStore,
    IHeaderStore? headerDb,
    [KeyFilter("blockInfos")] IDb? blockInfoDb,
    [KeyFilter("metadata")] IDb? metadataDb,
    IBadBlockStore? badBlockStore,
    IBlockAccessListStore? balStore,
    IChainLevelInfoRepository? chainLevelInfoRepository,
    ISpecProvider? specProvider,
    ISyncConfig? syncConfig,
    ILogManager? logManager,
    long genesisBlockNumber = 0) : BlockTree(blockStore, headerDb, blockInfoDb, metadataDb, badBlockStore,
        balStore, chainLevelInfoRepository, specProvider, syncConfig,
        logManager, genesisBlockNumber)
{
    private volatile bool _messEnabled;
    private long? _messActivateBlock;
    private long? _messDeactivateBlock;
    private long? _messOlympiaBlock;

    public void EnableMess(bool enable) => _messEnabled = enable;

    public bool IsMessEnabled => _messEnabled;

    public void SetMessBlockNumbers(long? activateBlock, long? deactivateBlock, long? olympiaBlock)
    {
        _messActivateBlock = activateBlock;
        _messDeactivateBlock = deactivateBlock;
        _messOlympiaBlock = olympiaBlock;
    }

    protected override bool HeadImprovementRequirementsSatisfied(BlockHeader header)
    {
        if (!base.HeadImprovementRequirementsSatisfied(header))
            return false;

        if (!_messEnabled || !IsMessActiveAtBlock(header.Number))
            return true;

        BlockHeader? currentHead = Head?.Header;
        if (currentHead is null)
            return true;

        BlockHeader? ancestor = FindCommonAncestor(currentHead, header);
        if (ancestor is null)
            return true;

        if (ancestor.Hash == currentHead.Hash)
            return true;

        UInt256 commonAncestorTD = ancestor.TotalDifficulty ?? UInt256.Zero;
        UInt256 localTD = currentHead.TotalDifficulty ?? UInt256.Zero;
        UInt256 proposedTD = header.TotalDifficulty ?? UInt256.Zero;

        if (MessCalculator.ShouldRejectReorg(
                commonAncestorTD,
                localTD,
                proposedTD,
                ancestor.Timestamp,
                currentHead.Timestamp))
        {
            if (Logger.IsInfo) Logger.Info(
                $"MESS rejected reorg: ancestor #{ancestor.Number} ({ancestor.Hash}), " +
                $"head #{currentHead.Number}, proposed #{header.Number} ({header.Hash}), " +
                $"timeDelta={currentHead.Timestamp - ancestor.Timestamp}s");
            return false;
        }

        return true;
    }

    /// <remarks>
    /// Active in [activateBlock, deactivateBlock) and again in [olympiaBlock, ∞).
    /// Returns false when no activation block is configured.
    /// </remarks>
    internal static bool IsMessActiveAtBlock(
        long blockNumber,
        long? activateBlock,
        long? deactivateBlock,
        long? olympiaBlock)
    {
        if (activateBlock is null || blockNumber < activateBlock.Value)
            return false;
        if (deactivateBlock is not null && blockNumber >= deactivateBlock.Value)
            return olympiaBlock is not null && blockNumber >= olympiaBlock.Value;
        return true;
    }

    internal bool IsMessActiveAtBlock(long blockNumber) =>
        IsMessActiveAtBlock(blockNumber, _messActivateBlock, _messDeactivateBlock, _messOlympiaBlock);

    private BlockHeader? FindCommonAncestor(BlockHeader a, BlockHeader b)
    {
        const int maxDepth = 8192;
        int steps = 0;

        BlockHeader? ha = a;
        BlockHeader? hb = b;

        while (ha is not null && hb is not null && ha.Number > hb.Number && steps < maxDepth)
        {
            ha = FindHeader(ha.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        while (hb is not null && ha is not null && hb.Number > ha.Number && steps < maxDepth)
        {
            hb = FindHeader(hb.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        while (ha is not null && hb is not null && ha.Hash != hb.Hash && steps < maxDepth)
        {
            ha = FindHeader(ha.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            hb = FindHeader(hb.ParentHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
            steps++;
        }

        if (ha is not null && hb is not null && ha.Hash == hb.Hash)
            return ha;

        return null;
    }
}

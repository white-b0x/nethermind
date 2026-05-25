// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Consensus.Rewards;
using Nethermind.Core;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Int256;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Reward calculator for Ethereum Classic implementing ECIP-1017 monetary policy.
/// <para>
/// At Olympia activation (ECIP-1111/1112), appends a treasury credit of
/// <c>baseFeePerGas × gasUsed</c> to the treasury address as an external reward.
/// </para>
/// Era period is configurable: ETC mainnet uses 5M blocks, Mordor uses 2M blocks.
/// </summary>
public class EtcRewardCalculator : IRewardCalculator, IRewardCalculatorSource
{
    private readonly long _eraPeriod;
    private readonly OlympiaParameters? _olympia;

    internal EtcRewardCalculator(long eraPeriod, OlympiaParameters? olympia = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eraPeriod);
        _eraPeriod = eraPeriod;
        _olympia = olympia;
    }

    public BlockReward[] CalculateRewards(Block block)
    {
        if (block.IsGenesis)
        {
            return [];
        }

        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(block.Number, _eraPeriod);
        BlockHeader blockHeader = block.Header;
        UInt256 mainReward = blockReward + (uint)block.Uncles.Length * (blockReward >> 5);
        long era = Ecip1017Calculator.GetEra(block.Number, _eraPeriod);

        bool hasTreasury = _olympia is not null
            && block.Number >= _olympia.Transition
            && block.BaseFeePerGas > UInt256.Zero
            && block.GasUsed > 0;

        BlockReward[] rewards = new BlockReward[1 + block.Uncles.Length + (hasTreasury ? 1 : 0)];
        rewards[0] = new BlockReward(blockHeader.Beneficiary, mainReward);

        for (int i = 0; i < block.Uncles.Length; i++)
        {
            UInt256 uncleReward = Ecip1017Calculator.CalculateUncleReward(
                blockReward, blockHeader.Number, block.Uncles[i].Number, era);
            rewards[i + 1] = new BlockReward(block.Uncles[i].Beneficiary, uncleReward, BlockRewardType.Uncle);
        }

        if (hasTreasury)
        {
            UInt256 treasuryCredit = block.BaseFeePerGas * (ulong)block.GasUsed;
            rewards[^1] = new BlockReward(_olympia!.TreasuryAddress, treasuryCredit, BlockRewardType.External);
        }

        return rewards;
    }

    public IRewardCalculator Get(ITransactionProcessor processor) => this;
}

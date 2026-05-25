// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Int256;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Pure calculation methods for ECIP-1017 monetary policy.
/// </summary>
public static class Ecip1017Calculator
{
    /// <summary>Base block reward: 5 ETC = 5 * 10^18 wei.</summary>
    public static readonly UInt256 BaseReward = 5_000_000_000_000_000_000;

    /// <summary>
    /// Calculates the block reward for a given block number according to ECIP-1017.
    /// Era 1: blocks 1 to eraPeriod → 5 ETC. Each subsequent era reduces by 20%.
    /// </summary>
    public static UInt256 CalculateBlockReward(long blockNumber, long eraPeriod)
    {
        if (blockNumber <= 0)
        {
            return BaseReward;
        }

        long era = (blockNumber - 1) / eraPeriod;

        UInt256 reward = BaseReward;
        for (long i = 0; i < era; i++)
        {
            reward = reward * 4 / 5;
        }

        return reward;
    }

    /// <summary>
    /// Gets the 0-indexed era number for a given block.
    /// </summary>
    public static long GetEra(long blockNumber, long eraPeriod)
    {
        if (blockNumber <= 0) return 0;
        return (blockNumber - 1) / eraPeriod;
    }

    /// <summary>
    /// Calculates the uncle reward according to ECIP-1017.
    /// Era 0: Standard Ethereum formula. Era 1+: Fixed 1/32 of block reward.
    /// </summary>
    public static UInt256 CalculateUncleReward(UInt256 blockReward, long blockNumber, long uncleNumber, long era)
    {
        if (era == 0)
        {
            return blockReward - ((uint)(blockNumber - uncleNumber) * blockReward >> 3);
        }

        return blockReward >> 5;
    }
}

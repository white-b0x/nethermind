// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class Ecip1017CalculatorTests
{
    private const long MainnetEra = 5_000_000;
    private const long MordorEra = 2_000_000;

    private static readonly object[] BlockRewardCases =
    [
        new object[] {         1L, MainnetEra, 5_000_000_000_000_000_000UL }, // Era 1
        new object[] { 5_000_001L, MainnetEra, 4_000_000_000_000_000_000UL }, // Era 2
        new object[] {10_000_001L, MainnetEra, 3_200_000_000_000_000_000UL }, // Era 3
        new object[] {15_000_001L, MainnetEra, 2_560_000_000_000_000_000UL }, // Era 4
        new object[] {20_000_001L, MainnetEra, 2_048_000_000_000_000_000UL }, // Era 5
        new object[] { 2_000_001L,  MordorEra, 4_000_000_000_000_000_000UL }, // Mordor Era 2
    ];

    [TestCaseSource(nameof(BlockRewardCases))]
    public void CalculateBlockReward_Returns_Expected_Value(long blockNumber, long eraPeriod, ulong expectedWei) =>
        Assert.That((ulong)Ecip1017Calculator.CalculateBlockReward(blockNumber, eraPeriod), Is.EqualTo(expectedWei));

    [Test]
    public void CalculateBlockReward_Era_Reduction_Is_20_Percent()
    {
        UInt256 era1 = Ecip1017Calculator.CalculateBlockReward(1, MainnetEra);
        UInt256 era2 = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);
        Assert.That(era2, Is.EqualTo(era1 * 4 / 5));
    }

    [Test]
    public void GetEra_Boundaries()
    {
        Assert.That(Ecip1017Calculator.GetEra(5_000_000, MainnetEra), Is.EqualTo(0)); // last of Era 1
        Assert.That(Ecip1017Calculator.GetEra(5_000_001, MainnetEra), Is.EqualTo(1)); // first of Era 2
        Assert.That(Ecip1017Calculator.GetEra(2_000_001, MordorEra), Is.EqualTo(1));  // Mordor Era 2
    }

    [Test]
    public void CalculateUncleReward_Era0_Decreases_With_Distance()
    {
        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(100, MainnetEra);

        UInt256 dist1 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 99, 0);
        UInt256 dist6 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 94, 0);

        Assert.That(dist1, Is.EqualTo(blockReward * 7 / 8));
        Assert.That(dist6, Is.EqualTo(blockReward * 2 / 8));
    }

    [Test]
    public void CalculateUncleReward_Era1Plus_Is_Fixed_OneThirtySecond()
    {
        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);
        UInt256 reward = Ecip1017Calculator.CalculateUncleReward(blockReward, 5_000_100, 5_000_099, 1);
        Assert.That(reward, Is.EqualTo(blockReward >> 5));
    }
}

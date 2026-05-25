// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
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
    public void CalculateBlockReward_Returns_Expected_Value(long blockNumber, long eraPeriod, ulong expectedWei) => ((ulong)Ecip1017Calculator.CalculateBlockReward(blockNumber, eraPeriod)).Should().Be(expectedWei);

    [Test]
    public void CalculateBlockReward_Era_Reduction_Is_20_Percent()
    {
        UInt256 era1 = Ecip1017Calculator.CalculateBlockReward(1, MainnetEra);
        UInt256 era2 = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);
        era2.Should().Be(era1 * 4 / 5);
    }

    [Test]
    public void GetEra_Boundaries()
    {
        Ecip1017Calculator.GetEra(5_000_000, MainnetEra).Should().Be(0); // last of Era 1
        Ecip1017Calculator.GetEra(5_000_001, MainnetEra).Should().Be(1); // first of Era 2
        Ecip1017Calculator.GetEra(2_000_001, MordorEra).Should().Be(1);  // Mordor Era 2
    }

    [Test]
    public void CalculateUncleReward_Era0_Decreases_With_Distance()
    {
        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(100, MainnetEra);

        UInt256 dist1 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 99, 0);
        UInt256 dist6 = Ecip1017Calculator.CalculateUncleReward(blockReward, 100, 94, 0);

        dist1.Should().Be(blockReward * 7 / 8);
        dist6.Should().Be(blockReward * 2 / 8);
    }

    [Test]
    public void CalculateUncleReward_Era1Plus_Is_Fixed_OneThirtySecond()
    {
        UInt256 blockReward = Ecip1017Calculator.CalculateBlockReward(5_000_001, MainnetEra);
        UInt256 reward = Ecip1017Calculator.CalculateUncleReward(blockReward, 5_000_100, 5_000_099, 1);
        reward.Should().Be(blockReward >> 5);
    }
}

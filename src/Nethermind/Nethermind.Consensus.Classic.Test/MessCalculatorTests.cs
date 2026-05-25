// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class MessCalculatorTests
{
    [Test]
    public void PolynomialV_AtZero_Returns128() => MessCalculator.PolynomialV(0).Should().Be((UInt256)128);

    [Test]
    public void PolynomialV_AtXCap_Returns3968() =>
        // At xcap=25132: denominator(128) + height(3840) = 3968
        MessCalculator.PolynomialV(25132).Should().Be((UInt256)3968);

    [Test]
    public void PolynomialV_BeyondXCap_CapsAt3968() => MessCalculator.PolynomialV(100_000).Should().Be((UInt256)3968);

    [Test]
    public void PolynomialV_MonotonicallyIncreasing()
    {
        UInt256 prev = MessCalculator.PolynomialV(0);
        for (ulong t = 100; t <= 25132; t += 100)
        {
            UInt256 current = MessCalculator.PolynomialV(t);
            current.Should().BeGreaterThanOrEqualTo(prev,
                $"PolynomialV should be monotonically increasing at t={t}");
            prev = current;
        }
    }

    [Test]
    public void PolynomialV_AtMidpoint_ReturnsExpectedValue() =>
        // At x=12566 (half of xcap) the S-curve sits at approximately half height (2048).
        MessCalculator.PolynomialV(12566).Should().Be((UInt256)2048);

    [Test]
    public void ShouldRejectReorg_DeepFork_Rejected()
    {
        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD: 1_000_000,
            localTD: 1_100_000,
            proposedTD: 1_100_001, // barely more TD
            commonAncestorTime: 1_000_000,
            currentHeadTime: 1_007_200); // 2 hours later

        rejected.Should().BeTrue("a deep fork with barely more TD should be rejected");
    }

    [Test]
    public void ShouldRejectReorg_ShallowFork_Accepted()
    {
        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD: 1_000_000,
            localTD: 1_000_100,
            proposedTD: 1_000_200,
            commonAncestorTime: 1_000_000,
            currentHeadTime: 1_000_013); // 13s — ~1 block ago

        rejected.Should().BeFalse("a shallow fork with more TD should be accepted");
    }

    [Test]
    public void ShouldRejectReorg_ChainExtension_NeverRejected()
    {
        // common ancestor IS the current head → localSubchainTD = 0, never rejected
        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD: 1_000_000,
            localTD: 1_000_000,
            proposedTD: 1_000_100,
            commonAncestorTime: 1_000_000,
            currentHeadTime: 1_000_000);

        rejected.Should().BeFalse("chain extension should never be rejected");
    }

    [Test]
    public void ShouldRejectReorg_EqualTD_Rejected()
    {
        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD: 1_000_000,
            localTD: 1_100_000,
            proposedTD: 1_100_000, // equal TD
            commonAncestorTime: 1_000_000,
            currentHeadTime: 1_001_000);

        rejected.Should().BeTrue("equal TD with time delta should be rejected");
    }

    [Test]
    public void ShouldRejectReorg_MassivelyHigherTD_Accepted()
    {
        // Even at max antigravity (31:1), 32x more subchain TD should overcome it.
        bool rejected = MessCalculator.ShouldRejectReorg(
            commonAncestorTD: 1_000_000,
            localTD: 1_100_000,
            proposedTD: 4_200_000, // +3.2M subchain TD
            commonAncestorTime: 1_000_000,
            currentHeadTime: 1_030_000);

        rejected.Should().BeFalse("massively higher TD should overcome antigravity");
    }
}

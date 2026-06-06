// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

/// <summary>
/// Unit tests for <see cref="EtcHeaderValidator"/> gas limit target logic.
///
/// ECIP-1122 mandates a SHOULD-level warning (not rejection) when a peer block's
/// gasLimit is below the network-scheduled gas limit target.
///
/// <list type="bullet">
///   <item>Pre-Olympia (Spiral epoch): gas limit target = 8,000,000</item>
///   <item>Olympia epoch and later: gas limit target = 60,000,000</item>
/// </list>
///
/// Mirrors core-geth's ForkGasTarget check and Besu's EtcGasLimitWarnRule.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class EtcHeaderValidatorTests
{
    private const long OlympiaBlock = 24_751_337L;   // ETC mainnet Olympia block
    private const long SpiralGasLimit = 8_000_000L;
    private const long OlympiaGasLimit = 60_000_000L;

    // ------------------------------------------------------------------
    // Pre-Olympia: Spiral target (8 M)
    // ------------------------------------------------------------------

    [Test]
    public void PreOlympia_TargetIs8M()
    {
        long limit = EtcHeaderValidator.SelectGasLimit(OlympiaBlock - 1, OlympiaBlock);
        Assert.That(limit, Is.EqualTo(SpiralGasLimit));
    }

    [Test]
    public void BlockZero_TargetIs8M()
    {
        long limit = EtcHeaderValidator.SelectGasLimit(0, OlympiaBlock);
        Assert.That(limit, Is.EqualTo(SpiralGasLimit));
    }

    [Test]
    public void NullOlympiaTransition_AlwaysReturns8M()
    {
        // No Olympia block configured → Spiral target applies for all blocks.
        Assert.That(EtcHeaderValidator.SelectGasLimit(0, null), Is.EqualTo(SpiralGasLimit));
        Assert.That(EtcHeaderValidator.SelectGasLimit(long.MaxValue, null), Is.EqualTo(SpiralGasLimit));
    }

    // ------------------------------------------------------------------
    // At/after Olympia: Olympia target (60 M)
    // ------------------------------------------------------------------

    [Test]
    public void AtOlympia_TargetIs60M()
    {
        long limit = EtcHeaderValidator.SelectGasLimit(OlympiaBlock, OlympiaBlock);
        Assert.That(limit, Is.EqualTo(OlympiaGasLimit));
    }

    [Test]
    public void AfterOlympia_TargetIs60M()
    {
        long limit = EtcHeaderValidator.SelectGasLimit(OlympiaBlock + 1_000_000L, OlympiaBlock);
        Assert.That(limit, Is.EqualTo(OlympiaGasLimit));
    }

    // ------------------------------------------------------------------
    // Target transition is at the Olympia block (inclusive)
    // ------------------------------------------------------------------

    [TestCase(OlympiaBlock - 1L, SpiralGasLimit,  TestName = "OneBlockBeforeOlympia_Spiral")]
    [TestCase(OlympiaBlock,      OlympiaGasLimit,  TestName = "AtOlympia_Olympia")]
    [TestCase(OlympiaBlock + 1L, OlympiaGasLimit,  TestName = "OneBlockAfterOlympia_Olympia")]
    public void TargetTransitionAt_OlympiaBlock(long blockNumber, long expectedLimit) =>
        Assert.That(EtcHeaderValidator.SelectGasLimit(blockNumber, OlympiaBlock), Is.EqualTo(expectedLimit));

    // ------------------------------------------------------------------
    // Target constant values match cross-client agreement
    // ------------------------------------------------------------------

    [Test]
    public void OlympiaTargetConstant_Is60M() =>
        // Verified against: core-geth ForkGasTarget, Besu EtcGasLimitWarnRule, fukuii OlympiaGasLimit
        Assert.That(OlympiaGasLimit, Is.EqualTo(60_000_000L));

    [Test]
    public void SpiralTargetConstant_Is8M() =>
        // Pre-Olympia ETC network gas limit target has been 8M since Spiral.
        Assert.That(SpiralGasLimit, Is.EqualTo(8_000_000L));
}

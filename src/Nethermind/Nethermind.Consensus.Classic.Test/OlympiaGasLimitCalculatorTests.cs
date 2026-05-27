// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Core;
using Nethermind.Core.Test.Builders;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class OlympiaGasLimitCalculatorTests
{
    private const long OlympiaBlock = 19_250_001; // example Mordor-scale transition
    private const long OlympiaGasTarget = 60_000_000;
    private const long GasLimitBoundDivisor = 1024;

    private static BlockHeader ParentAt(long blockNumber, long gasLimit) =>
        Build.A.BlockHeader.WithNumber(blockNumber).WithGasLimit(gasLimit).TestObject;

    // ------------------------------------------------------------------
    // Pre-Olympia: gas limit must be returned unchanged
    // ------------------------------------------------------------------

    private static readonly object[] PreOlympiaCases =
    [
        new object[] { 8_000_000L },
        new object[] { 5_000L },          // genesis-era
        new object[] { 30_000_000L },     // hypothetically high
        new object[] { OlympiaGasTarget },
    ];

    [TestCaseSource(nameof(PreOlympiaCases))]
    public void PreOlympia_Returns_Parent_Gas_Limit_Unchanged(long gasLimit)
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        // Block at OlympiaBlock - 1 means next block is OlympiaBlock - 1 + 1 = OlympiaBlock, but
        // the pre-Olympia branch fires when parentHeader.Number + 1 < OlympiaBlock.
        BlockHeader parent = ParentAt(OlympiaBlock - 2, gasLimit); // next block = OlympiaBlock - 1
        Assert.That(calc.GetGasLimit(parent), Is.EqualTo(gasLimit));
    }

    [Test]
    public void PreOlympia_One_Block_Before_Activation_Returns_Unchanged()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        BlockHeader parent = ParentAt(OlympiaBlock - 2, 8_000_000); // block OlympiaBlock - 1 produced
        Assert.That(calc.GetGasLimit(parent), Is.EqualTo(8_000_000));
    }

    // ------------------------------------------------------------------
    // Activation block: first block AT or AFTER transition starts growing
    // ------------------------------------------------------------------

    [Test]
    public void AtActivation_Starts_Increasing_Toward_60M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        BlockHeader parent = ParentAt(OlympiaBlock - 1, 8_000_000); // next block = OlympiaBlock
        long result = calc.GetGasLimit(parent);
        Assert.That(result, Is.GreaterThan(8_000_000).And.LessThanOrEqualTo(OlympiaGasTarget));
    }

    // ------------------------------------------------------------------
    // Convergence: from 8M, gas limit grows toward 60M
    // ------------------------------------------------------------------

    [Test]
    public void ConvergesFrom8M_To_60M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        long gasLimit = 8_000_000;
        long blockNumber = OlympiaBlock - 1;
        int steps = 0;

        while (gasLimit < OlympiaGasTarget)
        {
            gasLimit = calc.GetGasLimit(ParentAt(blockNumber, gasLimit));
            blockNumber++;
            steps++;
            Assert.That(steps, Is.LessThan(10_000), "convergence must complete within 10,000 blocks");
        }

        Assert.That(gasLimit, Is.EqualTo(OlympiaGasTarget));
        // Expected convergence: delta = 8M/1024 - 1 ≈ 7,811 gas/block → ~6,660 blocks
        Assert.That(steps, Is.LessThan(7_000));
    }

    [Test]
    public void ConvergesFrom8M_Reaches_Exactly_60M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        long gasLimit = 8_000_000;
        long blockNumber = OlympiaBlock - 1;

        for (int i = 0; i < 100_000 && gasLimit != OlympiaGasTarget; i++)
        {
            gasLimit = calc.GetGasLimit(ParentAt(blockNumber++, gasLimit));
        }

        Assert.That(gasLimit, Is.EqualTo(OlympiaGasTarget), "must converge to exactly 60M without overshooting");
    }

    // ------------------------------------------------------------------
    // Already at target: no movement
    // ------------------------------------------------------------------

    [Test]
    public void At60M_Returns_60M_Unchanged()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        BlockHeader parent = ParentAt(OlympiaBlock + 1000, OlympiaGasTarget);
        Assert.That(calc.GetGasLimit(parent), Is.EqualTo(OlympiaGasTarget));
    }

    // ------------------------------------------------------------------
    // Above target: decreases toward 60M
    // ------------------------------------------------------------------

    [Test]
    public void Above60M_Decreases_Toward_60M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        BlockHeader parent = ParentAt(OlympiaBlock + 100, 80_000_000);
        long result = calc.GetGasLimit(parent);
        Assert.That(result, Is.LessThan(80_000_000).And.GreaterThanOrEqualTo(OlympiaGasTarget));
    }

    [Test]
    public void ConvergesDown_From_Above_To_60M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        long gasLimit = 80_000_000;
        long blockNumber = OlympiaBlock;

        while (gasLimit > OlympiaGasTarget)
        {
            gasLimit = calc.GetGasLimit(ParentAt(blockNumber++, gasLimit));
        }

        Assert.That(gasLimit, Is.EqualTo(OlympiaGasTarget));
    }

    // ------------------------------------------------------------------
    // Null transition: no targeting, always returns parent gas limit
    // ------------------------------------------------------------------

    [Test]
    public void NullTransition_Always_Returns_Parent_Gas_Limit()
    {
        OlympiaGasLimitCalculator calc = new(null);
        foreach (long gasLimit in new[] { 8_000_000L, 60_000_000L, 1L, long.MaxValue / 2 })
        {
            Assert.That(calc.GetGasLimit(ParentAt(99_999_999, gasLimit)), Is.EqualTo(gasLimit));
        }
    }

    // ------------------------------------------------------------------
    // Delta calculation: correct per Yellow Paper (parentGasLimit / 1024 - 1)
    // ------------------------------------------------------------------

    [Test]
    public void Delta_Is_CorrectPerYellowPaper_At_8M()
    {
        OlympiaGasLimitCalculator calc = new(OlympiaBlock);
        long parent = 8_000_000;
        long expectedDelta = Math.Max(parent / GasLimitBoundDivisor - 1, 1); // 7811
        BlockHeader header = ParentAt(OlympiaBlock - 1, parent);
        long result = calc.GetGasLimit(header);
        Assert.That(result - parent, Is.EqualTo(expectedDelta));
    }
}

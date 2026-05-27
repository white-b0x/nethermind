// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class EtchashEpochCalculatorTests
{
    private const long Ecip1099Transition = 11_700_000;

    private readonly EtchashEpochCalculator _calculator = new(Ecip1099Transition);

    [Test]
    public void GetCacheEpoch_BeforeTransition_UsesEthashEpochAsSeed() => Assert.That(
        _calculator.GetCacheEpoch(Ecip1099Transition - 1),
        Is.EqualTo(new EtchashCacheEpoch(DagEpoch: 389, SeedEpoch: 389)));

    [Test]
    public void GetCacheEpoch_AtTransition_UsesHalvedDagEpochAndDoubledSeed() => Assert.That(
        _calculator.GetCacheEpoch(Ecip1099Transition),
        Is.EqualTo(new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390)));

    [Test]
    public void GetCacheEpochs_CrossingTransition_ReturnsBothSides() => Assert.That(
        _calculator.GetCacheEpochs(Ecip1099Transition - 1, Ecip1099Transition),
        Is.EqualTo(new[]
        {
            new EtchashCacheEpoch(DagEpoch: 389, SeedEpoch: 389),
            new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390),
        }));

    [Test]
    public void GetCacheEpochs_AfterTransition_SkipsUnusedOddSeedEpochs() => Assert.That(
        _calculator.GetCacheEpochs(Ecip1099Transition, Ecip1099Transition + 120_000),
        Is.EqualTo(new[]
        {
            new EtchashCacheEpoch(DagEpoch: 195, SeedEpoch: 390),
            new EtchashCacheEpoch(DagEpoch: 196, SeedEpoch: 392),
            new EtchashCacheEpoch(DagEpoch: 197, SeedEpoch: 394),
        }));

    [Test]
    public void GetCacheEpochs_RejectsOverlyWideRanges()
    {
        Action act = () => _calculator.GetCacheEpochs(0, 330_000, maxEpochs: 11);
        Assert.That(act, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Hint too wide"));
    }
}

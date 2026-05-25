// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
public class EtchashHintBasedCacheTests
{
    [Test]
    public void Hint_AddsAndRemovesEpochRefsPerGuid()
    {
        EtchashHintBasedCache cache = new(_ => new TestDataSet());
        Guid g1 = Guid.NewGuid();
        Guid g2 = Guid.NewGuid();
        EtchashCacheEpoch epoch = new(DagEpoch: 195, SeedEpoch: 390);

        cache.Hint(g1, [epoch]);
        cache.Hint(g2, [epoch]);

        cache.CachedEpochsCount.Should().Be(1);
        cache.Get(epoch).Should().NotBeNull();

        cache.Hint(g1, []);
        cache.CachedEpochsCount.Should().Be(1);

        cache.Hint(g2, []);
        cache.CachedEpochsCount.Should().Be(0);
        cache.Get(epoch).Should().BeNull();
    }

    [Test]
    public void Hint_ReusesRecentlyReleasedEpoch()
    {
        int builds = 0;
        EtchashHintBasedCache cache = new(_ => { builds++; return new TestDataSet(); });
        Guid guid = Guid.NewGuid();
        EtchashCacheEpoch epoch = new(DagEpoch: 195, SeedEpoch: 390);

        cache.Hint(guid, [epoch]);
        IEthashDataSet? first = cache.Get(epoch);
        cache.Hint(guid, []);
        cache.Hint(guid, [epoch]);

        cache.Get(epoch).Should().BeSameAs(first);
        builds.Should().Be(1);
    }

    [Test]
    public void Hint_RejectsOverlyWideRanges()
    {
        EtchashHintBasedCache cache = new(_ => new TestDataSet());
        EtchashCacheEpoch[] epochs = Enumerable.Range(0, 12)
            .Select(i => new EtchashCacheEpoch((uint)i, (uint)i))
            .ToArray();

        Action act = () => cache.Hint(Guid.NewGuid(), epochs);
        act.Should().Throw<InvalidOperationException>().WithMessage("Hint too wide");
    }

    [Test]
    public void Dispose_Disposes_Cached_And_Recently_Released_Datasets()
    {
        List<TestDataSet> created = [];
        EtchashHintBasedCache cache = new(_ => { TestDataSet ds = new(); created.Add(ds); return ds; });
        Guid guid = Guid.NewGuid();
        EtchashCacheEpoch active = new(DagEpoch: 195, SeedEpoch: 390);
        EtchashCacheEpoch released = new(DagEpoch: 196, SeedEpoch: 392);

        cache.Hint(guid, [released]);
        cache.Get(released);
        cache.Hint(guid, [active]);
        cache.Get(active);

        created.Should().HaveCount(2);
        cache.Dispose();
        created.Should().AllSatisfy(ds => ds.DisposeCount.Should().Be(1));
    }

    [Test]
    public void Hint_Across_Ecip1099_Transition_Builds_Both_Sides()
    {
        const long ecip1099Transition = 11_700_000;
        EtchashEpochCalculator calculator = new(ecip1099Transition);
        EtchashHintBasedCache cache = new(epoch => new TestDataSet(epoch.SeedEpoch));
        IReadOnlyList<EtchashCacheEpoch> epochs =
            calculator.GetCacheEpochs(ecip1099Transition - 1, ecip1099Transition);

        cache.Hint(Guid.NewGuid(), epochs);

        cache.CachedEpochsCount.Should().Be(2);
        ((TestDataSet)cache.Get(epochs[0])!).SeedEpoch.Should().Be(389);
        ((TestDataSet)cache.Get(epochs[1])!).SeedEpoch.Should().Be(390);
    }

    private sealed class TestDataSet(uint seedEpoch = 0) : IEthashDataSet
    {
        public uint SeedEpoch { get; } = seedEpoch;
        public int DisposeCount { get; private set; }
        public uint Size => 0;
        public uint[] CalcDataSetItem(uint i) => [];
        public void Dispose() => DisposeCount++;
    }
}

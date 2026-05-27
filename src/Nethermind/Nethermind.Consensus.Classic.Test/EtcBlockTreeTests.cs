// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

/// <summary>
/// Tests for ECBP-1100 MESS block-number gating.
/// Verifies the three-phase lifecycle: first window [activate, deactivate),
/// inactive gap [deactivate, olympia), and reactivation [olympia, ∞).
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class EtcBlockTreeTests
{
    // activate=100, deactivate=200, olympia=300
    private static readonly object[] MessWindowCases =
    [
        new object[] {   0L, false }, // before activate
        new object[] {  99L, false }, // one before activate
        new object[] { 100L, true  }, // at activate (inclusive)
        new object[] { 150L, true  }, // mid first window
        new object[] { 199L, true  }, // one before deactivate
        new object[] { 200L, false }, // at deactivate (exclusive from first window)
        new object[] { 250L, false }, // in the gap
        new object[] { 299L, false }, // one before olympia
        new object[] { 300L, true  }, // at olympia reactivation (inclusive)
        new object[] { 999L, true  }, // well after olympia
    ];

    [TestCaseSource(nameof(MessWindowCases))]
    public void IsMessActiveAtBlock_ThreePhaseWindow(long blockNumber, bool expected) => Assert.That(
        EtcBlockTree.IsMessActiveAtBlock(blockNumber, activateBlock: 100, deactivateBlock: 200, olympiaBlock: 300),
        Is.EqualTo(expected), $"block {blockNumber}");

    [Test]
    public void IsMessActiveAtBlock_NullActivate_AlwaysInactive()
    {
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(0, null, null, null), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(long.MaxValue, null, null, null), Is.False);
    }

    [Test]
    public void IsMessActiveAtBlock_NoDeactivate_ActiveFromActivateOnward()
    {
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(99, activateBlock: 100, deactivateBlock: null, olympiaBlock: null), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(100, activateBlock: 100, deactivateBlock: null, olympiaBlock: null), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(long.MaxValue, 100, null, null), Is.True);
    }

    [Test]
    public void IsMessActiveAtBlock_DeactivateWithoutOlympia_InactiveAfterDeactivate()
    {
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(150, 100, 200, null), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(200, 100, 200, null), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(long.MaxValue, 100, 200, null), Is.False);
    }

    [Test]
    public void IsMessActiveAtBlock_EtcMainnetRealBlockNumbers()
    {
        const long activate = 11_380_000;
        const long deactivate = 19_250_000;
        const long olympia = 1_000_000_000_000_000_000L; // 1e18 sentinel

        Assert.That(EtcBlockTree.IsMessActiveAtBlock(activate - 1, activate, deactivate, olympia), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(activate, activate, deactivate, olympia), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(deactivate - 1, activate, deactivate, olympia), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(deactivate, activate, deactivate, olympia), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(deactivate + 1_000_000, activate, deactivate, olympia), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(olympia, activate, deactivate, olympia), Is.True);
    }

    [Test]
    public void IsMessActiveAtBlock_MordorRealBlockNumbers()
    {
        const long activate = 2_380_000;
        const long deactivate = 10_400_000;
        const long olympia = 1_000_000_000_000_000_000L;

        Assert.That(EtcBlockTree.IsMessActiveAtBlock(activate - 1, activate, deactivate, olympia), Is.False);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(activate, activate, deactivate, olympia), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(deactivate - 1, activate, deactivate, olympia), Is.True);
        Assert.That(EtcBlockTree.IsMessActiveAtBlock(deactivate, activate, deactivate, olympia), Is.False);
    }
}

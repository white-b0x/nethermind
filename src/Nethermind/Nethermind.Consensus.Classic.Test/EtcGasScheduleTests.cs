// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Nethermind.Consensus;
using Nethermind.Core.Specs;
using Nethermind.Logging;
using Nethermind.Serialization.Json;
using Nethermind.Specs.ChainSpecStyle;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

/// <summary>
/// Verifies that gas-affecting EIP flags activate at the correct block numbers on ETC mainnet
/// and Mordor, and that PoS EIPs and other ETH-only features remain inactive throughout.
///
/// Covers the full ETC chain history from genesis through Spiral, and verifies Olympia sentinels
/// for EIP-1559, EIP-7825, and EIP-7623.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class EtcGasScheduleTests
{
    // ----------------------------------------------------------------
    // ETC mainnet fork blocks
    // ----------------------------------------------------------------
    private const long ClassicHomestead = 1_150_000;
    private const long ClassicTangerine = 2_500_000;
    private const long ClassicAtlantis = 8_772_000;
    private const long ClassicAgharta = 9_573_000;
    private const long ClassicPhoenix = 10_500_839;
    private const long ClassicMagneto = 13_189_133;
    private const long ClassicMystique = 14_525_000;
    private const long ClassicSpiral = 19_250_000;
    private const long ClassicOlympiaSentinel = 1_000_000_000_000_000_000L; // 0xDE0B6B3A7640000

    // ----------------------------------------------------------------
    // Mordor testnet fork blocks
    // ----------------------------------------------------------------
    private const long MordorHomestead = 0;
    private const long MordorAtlantis = 0;
    private const long MordorAgharta = 301_243;
    private const long MordorPhoenix = 999_983;
    private const long MordorMagneto = 3_985_893;
    private const long MordorMystique = 5_520_000;
    private const long MordorSpiral = 9_957_000;
    private const long MordorOlympiaSentinel = 1_000_000_000_000_000_000L;

    private static ChainSpecBasedSpecProvider LoadSpec(string chainName)
    {
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, "../../../../", $"Chains/{chainName}.json");
        using FileStream stream = File.OpenRead(path);
        ChainSpec chainSpec = new ChainSpecLoader(new EthereumJsonSerializer(), NullLogManager.Instance).Load(stream);
        return new ChainSpecBasedSpecProvider(chainSpec);
    }

    private static IReleaseSpec Spec(ChainSpecBasedSpecProvider provider, long blockNumber) =>
        provider.GetSpec(blockNumber, null);

    // ================================================================
    // ETC Mainnet — EIP activation boundaries
    // ================================================================

    [Test]
    public void Classic_EIP150_Activates_At_TangerineWhistle()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicTangerine - 1).IsEip150Enabled, Is.False);
        Assert.That(Spec(p, ClassicTangerine).IsEip150Enabled, Is.True);
    }

    [Test]
    public void Classic_EIP211_EIP214_Activate_At_Atlantis()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicAtlantis - 1).IsEip211Enabled, Is.False);
        Assert.That(Spec(p, ClassicAtlantis).IsEip211Enabled, Is.True);
        Assert.That(Spec(p, ClassicAtlantis).IsEip214Enabled, Is.True);
    }

    [Test]
    public void Classic_Agharta_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicAgharta - 1).IsEip145Enabled, Is.False);
        Assert.That(Spec(p, ClassicAgharta).IsEip145Enabled, Is.True);
        Assert.That(Spec(p, ClassicAgharta).IsEip1014Enabled, Is.True);
        Assert.That(Spec(p, ClassicAgharta).IsEip1052Enabled, Is.True);
    }

    [Test]
    public void Classic_Phoenix_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicPhoenix - 1).IsEip1108Enabled, Is.False);
        Assert.That(Spec(p, ClassicPhoenix).IsEip1108Enabled, Is.True);
        Assert.That(Spec(p, ClassicPhoenix).IsEip1884Enabled, Is.True);
        Assert.That(Spec(p, ClassicPhoenix).IsEip2028Enabled, Is.True);
        Assert.That(Spec(p, ClassicPhoenix).IsEip2200Enabled, Is.True);
    }

    [Test]
    public void Classic_Magneto_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicMagneto - 1).IsEip2565Enabled, Is.False);
        Assert.That(Spec(p, ClassicMagneto).IsEip2565Enabled, Is.True);
        Assert.That(Spec(p, ClassicMagneto).IsEip2929Enabled, Is.True);
    }

    [Test]
    public void Classic_Mystique_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicMystique - 1).IsEip3529Enabled, Is.False);
        Assert.That(Spec(p, ClassicMystique).IsEip3529Enabled, Is.True);
        Assert.That(Spec(p, ClassicMystique).IsEip3541Enabled, Is.True);
    }

    [Test]
    public void Classic_Mystique_EIP1559_Is_Suppressed_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        // EIP-1559 activates at Olympia (ECIP-1111); ApplyToReleaseSpec suppresses it until then.
        Assert.That(Spec(p, ClassicMystique - 1).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, ClassicMystique).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, ClassicSpiral).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, ClassicSpiral + 1_000_000).IsEip1559Enabled, Is.False);
    }

    [Test]
    public void Classic_Mystique_ElasticityMultiplier_Is_One_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        // Must be 1 so Eip1559GasLimitAdjuster does not double the gas limit at Mystique.
        Assert.That(Spec(p, ClassicMystique).ElasticityMultiplier, Is.EqualTo(1));
        Assert.That(Spec(p, ClassicSpiral).ElasticityMultiplier, Is.EqualTo(1));
        Assert.That(Spec(p, ClassicSpiral + 1_000_000).ElasticityMultiplier, Is.EqualTo(1));
    }

    [Test]
    public void Classic_Spiral_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicSpiral - 1).IsEip3651Enabled, Is.False);
        Assert.That(Spec(p, ClassicSpiral).IsEip3651Enabled, Is.True);
        Assert.That(Spec(p, ClassicSpiral).IsEip3855Enabled, Is.True);
        Assert.That(Spec(p, ClassicSpiral).IsEip3860Enabled, Is.True);
    }

    [Test]
    public void Classic_Olympia_EIP1559_Activates_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicOlympiaSentinel - 1).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, ClassicOlympiaSentinel).IsEip1559Enabled, Is.True);
        Assert.That(Spec(p, ClassicOlympiaSentinel).ElasticityMultiplier, Is.EqualTo(2));
    }

    [Test]
    public void Classic_Olympia_EIP7825_EIP7623_Activate_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, ClassicOlympiaSentinel - 1).IsEip7825Enabled, Is.False);
        Assert.That(Spec(p, ClassicOlympiaSentinel).IsEip7825Enabled, Is.True);
        Assert.That(Spec(p, ClassicOlympiaSentinel).IsEip7623Enabled, Is.True);
    }

    // ================================================================
    // Mordor testnet — same EIP set, different block numbers
    // ================================================================

    [Test]
    public void Mordor_Agharta_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorAgharta - 1).IsEip145Enabled, Is.False);
        Assert.That(Spec(p, MordorAgharta).IsEip145Enabled, Is.True);
        Assert.That(Spec(p, MordorAgharta).IsEip1014Enabled, Is.True);
    }

    [Test]
    public void Mordor_Phoenix_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorPhoenix - 1).IsEip1108Enabled, Is.False);
        Assert.That(Spec(p, MordorPhoenix).IsEip1108Enabled, Is.True);
        Assert.That(Spec(p, MordorPhoenix).IsEip2028Enabled, Is.True);
        Assert.That(Spec(p, MordorPhoenix).IsEip2200Enabled, Is.True);
    }

    [Test]
    public void Mordor_Magneto_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorMagneto - 1).IsEip2929Enabled, Is.False);
        Assert.That(Spec(p, MordorMagneto).IsEip2929Enabled, Is.True);
        Assert.That(Spec(p, MordorMagneto).IsEip2565Enabled, Is.True);
    }

    [Test]
    public void Mordor_Mystique_EIP1559_Is_Suppressed_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorMystique).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, MordorSpiral).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, MordorSpiral).ElasticityMultiplier, Is.EqualTo(1));
    }

    [Test]
    public void Mordor_Spiral_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorSpiral - 1).IsEip3651Enabled, Is.False);
        Assert.That(Spec(p, MordorSpiral).IsEip3651Enabled, Is.True);
        Assert.That(Spec(p, MordorSpiral).IsEip3855Enabled, Is.True);
        Assert.That(Spec(p, MordorSpiral).IsEip3860Enabled, Is.True);
    }

    [Test]
    public void Mordor_Olympia_EIP1559_Activates_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorOlympiaSentinel - 1).IsEip1559Enabled, Is.False);
        Assert.That(Spec(p, MordorOlympiaSentinel).IsEip1559Enabled, Is.True);
        Assert.That(Spec(p, MordorOlympiaSentinel).ElasticityMultiplier, Is.EqualTo(2));
    }

    [Test]
    public void Mordor_Olympia_EIP7825_EIP7623_Activate_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Assert.That(Spec(p, MordorOlympiaSentinel - 1).IsEip7825Enabled, Is.False);
        Assert.That(Spec(p, MordorOlympiaSentinel).IsEip7825Enabled, Is.True);
        Assert.That(Spec(p, MordorOlympiaSentinel).IsEip7623Enabled, Is.True);
    }

    // ================================================================
    // Gas limit adjuster: must NOT double at Olympia
    // ================================================================

    [Test]
    public void Classic_Olympia_GasLimitAdjuster_Does_Not_Double_At_Transition()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        IReleaseSpec spec = Spec(p, ClassicOlympiaSentinel);
        long adjusted = Eip1559GasLimitAdjuster.AdjustGasLimit(spec, 8_000_000, ClassicOlympiaSentinel);
        Assert.That(adjusted, Is.EqualTo(8_000_000), "ETC Olympia must not apply the 2x London gas limit doubling");
    }

    [Test]
    public void Mordor_Olympia_GasLimitAdjuster_Does_Not_Double_At_Transition()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        IReleaseSpec spec = Spec(p, MordorOlympiaSentinel);
        long adjusted = Eip1559GasLimitAdjuster.AdjustGasLimit(spec, 8_000_000, MordorOlympiaSentinel);
        Assert.That(adjusted, Is.EqualTo(8_000_000), "ETC Olympia must not apply the 2x London gas limit doubling");
    }

    // ================================================================
    // ETC-specific invariants: features that must NEVER be active
    // ================================================================

    private static readonly long[] EtcMainnetCheckpoints =
    [
        1L, ClassicHomestead, ClassicAtlantis, ClassicAgharta,
        ClassicPhoenix, ClassicMagneto, ClassicMystique, ClassicSpiral,
        ClassicSpiral + 1_000_000,
    ];

    [TestCaseSource(nameof(EtcMainnetCheckpoints))]
    public void Classic_EIP1283_Is_Never_Active(long blockNumber)
    {
        // EIP-1283 (net-metered SSTORE) was reverted by Petersburg at the same block as
        // Constantinople (Agharta). It must never be active on ETC.
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, blockNumber).IsEip1283Enabled, Is.False);
    }

    [TestCaseSource(nameof(EtcMainnetCheckpoints))]
    public void Classic_EIP4895_Withdrawals_Never_Active(long blockNumber)
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, blockNumber).IsEip4895Enabled, Is.False);
    }

    [TestCaseSource(nameof(EtcMainnetCheckpoints))]
    public void Classic_EIP3198_BasefeeOpcode_Suppressed_Pre_Olympia(long blockNumber)
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Assert.That(Spec(p, blockNumber).IsEip3198Enabled, Is.False);
    }
}

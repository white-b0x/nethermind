// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using FluentAssertions;
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
        Spec(p, ClassicTangerine - 1).IsEip150Enabled.Should().BeFalse();
        Spec(p, ClassicTangerine).IsEip150Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_EIP211_EIP214_Activate_At_Atlantis()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicAtlantis - 1).IsEip211Enabled.Should().BeFalse();
        Spec(p, ClassicAtlantis).IsEip211Enabled.Should().BeTrue();
        Spec(p, ClassicAtlantis).IsEip214Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Agharta_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicAgharta - 1).IsEip145Enabled.Should().BeFalse();
        Spec(p, ClassicAgharta).IsEip145Enabled.Should().BeTrue();
        Spec(p, ClassicAgharta).IsEip1014Enabled.Should().BeTrue();
        Spec(p, ClassicAgharta).IsEip1052Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Phoenix_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicPhoenix - 1).IsEip1108Enabled.Should().BeFalse();
        Spec(p, ClassicPhoenix).IsEip1108Enabled.Should().BeTrue();
        Spec(p, ClassicPhoenix).IsEip1884Enabled.Should().BeTrue();
        Spec(p, ClassicPhoenix).IsEip2028Enabled.Should().BeTrue();
        Spec(p, ClassicPhoenix).IsEip2200Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Magneto_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicMagneto - 1).IsEip2565Enabled.Should().BeFalse();
        Spec(p, ClassicMagneto).IsEip2565Enabled.Should().BeTrue();
        Spec(p, ClassicMagneto).IsEip2929Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Mystique_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicMystique - 1).IsEip3529Enabled.Should().BeFalse();
        Spec(p, ClassicMystique).IsEip3529Enabled.Should().BeTrue();
        Spec(p, ClassicMystique).IsEip3541Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Mystique_EIP1559_Is_Suppressed_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        // EIP-1559 activates at Olympia (ECIP-1111); ApplyToReleaseSpec suppresses it until then.
        Spec(p, ClassicMystique - 1).IsEip1559Enabled.Should().BeFalse();
        Spec(p, ClassicMystique).IsEip1559Enabled.Should().BeFalse();
        Spec(p, ClassicSpiral).IsEip1559Enabled.Should().BeFalse();
        Spec(p, ClassicSpiral + 1_000_000).IsEip1559Enabled.Should().BeFalse();
    }

    [Test]
    public void Classic_Mystique_ElasticityMultiplier_Is_One_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        // Must be 1 so Eip1559GasLimitAdjuster does not double the gas limit at Mystique.
        Spec(p, ClassicMystique).ElasticityMultiplier.Should().Be(1);
        Spec(p, ClassicSpiral).ElasticityMultiplier.Should().Be(1);
        Spec(p, ClassicSpiral + 1_000_000).ElasticityMultiplier.Should().Be(1);
    }

    [Test]
    public void Classic_Spiral_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicSpiral - 1).IsEip3651Enabled.Should().BeFalse();
        Spec(p, ClassicSpiral).IsEip3651Enabled.Should().BeTrue();
        Spec(p, ClassicSpiral).IsEip3855Enabled.Should().BeTrue();
        Spec(p, ClassicSpiral).IsEip3860Enabled.Should().BeTrue();
    }

    [Test]
    public void Classic_Olympia_EIP1559_Activates_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicOlympiaSentinel - 1).IsEip1559Enabled.Should().BeFalse();
        Spec(p, ClassicOlympiaSentinel).IsEip1559Enabled.Should().BeTrue();
        Spec(p, ClassicOlympiaSentinel).ElasticityMultiplier.Should().Be(2);
    }

    [Test]
    public void Classic_Olympia_EIP7825_EIP7623_Activate_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, ClassicOlympiaSentinel - 1).IsEip7825Enabled.Should().BeFalse();
        Spec(p, ClassicOlympiaSentinel).IsEip7825Enabled.Should().BeTrue();
        Spec(p, ClassicOlympiaSentinel).IsEip7623Enabled.Should().BeTrue();
    }

    // ================================================================
    // Mordor testnet — same EIP set, different block numbers
    // ================================================================

    [Test]
    public void Mordor_Agharta_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorAgharta - 1).IsEip145Enabled.Should().BeFalse();
        Spec(p, MordorAgharta).IsEip145Enabled.Should().BeTrue();
        Spec(p, MordorAgharta).IsEip1014Enabled.Should().BeTrue();
    }

    [Test]
    public void Mordor_Phoenix_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorPhoenix - 1).IsEip1108Enabled.Should().BeFalse();
        Spec(p, MordorPhoenix).IsEip1108Enabled.Should().BeTrue();
        Spec(p, MordorPhoenix).IsEip2028Enabled.Should().BeTrue();
        Spec(p, MordorPhoenix).IsEip2200Enabled.Should().BeTrue();
    }

    [Test]
    public void Mordor_Magneto_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorMagneto - 1).IsEip2929Enabled.Should().BeFalse();
        Spec(p, MordorMagneto).IsEip2929Enabled.Should().BeTrue();
        Spec(p, MordorMagneto).IsEip2565Enabled.Should().BeTrue();
    }

    [Test]
    public void Mordor_Mystique_EIP1559_Is_Suppressed_Pre_Olympia()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorMystique).IsEip1559Enabled.Should().BeFalse();
        Spec(p, MordorSpiral).IsEip1559Enabled.Should().BeFalse();
        Spec(p, MordorSpiral).ElasticityMultiplier.Should().Be(1);
    }

    [Test]
    public void Mordor_Spiral_EIPs_Activate_At_CorrectBlock()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorSpiral - 1).IsEip3651Enabled.Should().BeFalse();
        Spec(p, MordorSpiral).IsEip3651Enabled.Should().BeTrue();
        Spec(p, MordorSpiral).IsEip3855Enabled.Should().BeTrue();
        Spec(p, MordorSpiral).IsEip3860Enabled.Should().BeTrue();
    }

    [Test]
    public void Mordor_Olympia_EIP1559_Activates_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorOlympiaSentinel - 1).IsEip1559Enabled.Should().BeFalse();
        Spec(p, MordorOlympiaSentinel).IsEip1559Enabled.Should().BeTrue();
        Spec(p, MordorOlympiaSentinel).ElasticityMultiplier.Should().Be(2);
    }

    [Test]
    public void Mordor_Olympia_EIP7825_EIP7623_Activate_At_Sentinel()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        Spec(p, MordorOlympiaSentinel - 1).IsEip7825Enabled.Should().BeFalse();
        Spec(p, MordorOlympiaSentinel).IsEip7825Enabled.Should().BeTrue();
        Spec(p, MordorOlympiaSentinel).IsEip7623Enabled.Should().BeTrue();
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
        adjusted.Should().Be(8_000_000, "ETC Olympia must not apply the 2x London gas limit doubling");
    }

    [Test]
    public void Mordor_Olympia_GasLimitAdjuster_Does_Not_Double_At_Transition()
    {
        ChainSpecBasedSpecProvider p = LoadSpec("mordor");
        IReleaseSpec spec = Spec(p, MordorOlympiaSentinel);
        long adjusted = Eip1559GasLimitAdjuster.AdjustGasLimit(spec, 8_000_000, MordorOlympiaSentinel);
        adjusted.Should().Be(8_000_000, "ETC Olympia must not apply the 2x London gas limit doubling");
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
        Spec(p, blockNumber).IsEip1283Enabled.Should().BeFalse();
    }

    [TestCaseSource(nameof(EtcMainnetCheckpoints))]
    public void Classic_EIP4895_Withdrawals_Never_Active(long blockNumber)
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, blockNumber).IsEip4895Enabled.Should().BeFalse();
    }

    [TestCaseSource(nameof(EtcMainnetCheckpoints))]
    public void Classic_EIP3198_BasefeeOpcode_Suppressed_Pre_Olympia(long blockNumber)
    {
        ChainSpecBasedSpecProvider p = LoadSpec("classic");
        Spec(p, blockNumber).IsEip3198Enabled.Should().BeFalse();
    }
}

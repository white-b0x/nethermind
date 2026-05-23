// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Specs;
using Nethermind.Specs.ChainSpecStyle;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Chain spec engine parameters for Ethereum Classic (Etchash).
/// Inherits from EthashChainSpecEngineParameters and re-implements IChainSpecEngineParameters
/// to customize Fork ID calculation per ECIP-1082/ECIP-1091 and add Olympia parameters.
/// </summary>
public class EtchashChainSpecEngineParameters : EthashChainSpecEngineParameters, IChainSpecEngineParameters
{
    string? IChainSpecEngineParameters.EngineName => "Etchash";
    string? IChainSpecEngineParameters.SealEngineType => "Etchash";

    /// <summary>Block number at which ECIP-1099 (Thanos) activates. ETC mainnet: 11,700,000.</summary>
    public long? Ecip1099Transition { get; set; }

    /// <summary>Block number for EIP-3855 (PUSH0) — Spiral fork.</summary>
    public long? Eip3855Transition { get; set; }

    /// <summary>Block number for EIP-3860 (initcode size limit) — Spiral fork.</summary>
    public long? Eip3860Transition { get; set; }

    /// <summary>Block number for EIP-3651 (warm COINBASE) — Spiral fork.</summary>
    public long? Eip3651Transition { get; set; }

    /// <summary>ECIP-1017 era length in blocks. ETC mainnet: 5,000,000. Mordor: 2,000,000.</summary>
    public long Ecip1017EraRounds { get; set; }

    /// <summary>Block at which Die Hard activates (bomb paused). ETC mainnet: 3,000,000. Null = bomb never existed.</summary>
    public long? DieHardTransition { get; set; }

    /// <summary>Block at which Gotham activates (bomb delayed). ETC mainnet: 5,000,000. Null = not applicable.</summary>
    public long? GothamTransition { get; set; }

    /// <summary>Block at which ECIP-1041 activates (bomb removed). ETC mainnet: 5,900,000. Null = not applicable.</summary>
    public long? Ecip1041Transition { get; set; }

    /// <summary>Block at which ECBP-1100 MESS activates. ETC mainnet: 11,380,000. Mordor: 2,380,000.</summary>
    public long? Ecbp1100Transition { get; set; }

    /// <summary>Block at which ECBP-1100 MESS deactivates. ETC mainnet: 19,250,000. Mordor: 10,400,000.</summary>
    public long? Ecbp1100DeactivateTransition { get; set; }

    /// <summary>Block at which Olympia activates (ECIP-1111/1112/1121). Sentinel: 1e18 (not yet scheduled).</summary>
    public long? OlympiaTransition { get; set; }

    /// <summary>Treasury address for ECIP-1112 basefee redirect. Canonical: 0xd6165F3aF4281037bce810621F62B43077Fb0e37.</summary>
    public Address? OlympiaTreasuryAddress { get; set; }

    /// <remarks>
    /// Per ECIP-1082/ECIP-1091: block reward reductions (ECIP-1017) must NOT affect Fork ID.
    /// Excludes BlockReward entries from fork transitions.
    /// OlympiaTransition is added so peers can identify the Olympia Fork ID checkpoint.
    /// </remarks>
    void IChainSpecEngineParameters.AddTransitions(SortedSet<long> blockNumbers, SortedSet<ulong> timestamps)
    {
        if (DifficultyBombDelays is not null)
        {
            foreach ((long blockNumber, _) in DifficultyBombDelays)
            {
                blockNumbers.Add(blockNumber);
            }
        }

        // BlockReward changes intentionally NOT added per ECIP-1082

        blockNumbers.Add(HomesteadTransition);
        if (DaoHardforkTransition is not null) blockNumbers.Add(DaoHardforkTransition.Value);
        if (Eip100bTransition is not null) blockNumbers.Add(Eip100bTransition.Value);
        if (Ecip1099Transition is not null) blockNumbers.Add(Ecip1099Transition.Value);
        if (Eip3651Transition is not null) blockNumbers.Add(Eip3651Transition.Value);
        if (Eip3855Transition is not null) blockNumbers.Add(Eip3855Transition.Value);
        if (Eip3860Transition is not null) blockNumbers.Add(Eip3860Transition.Value);
        if (OlympiaTransition is not null) blockNumbers.Add(OlympiaTransition.Value);
    }

    void IChainSpecEngineParameters.ApplyToChainSpec(ChainSpec chainSpec)
    {
        // ETC doesn't have Muir Glacier, Arrow Glacier, or Gray Glacier
        chainSpec.MuirGlacierNumber = null;
        chainSpec.ArrowGlacierBlockNumber = null;
        chainSpec.GrayGlacierBlockNumber = null;
        chainSpec.HomesteadBlockNumber = HomesteadTransition;
        chainSpec.DaoForkBlockNumber = DaoHardforkTransition;

        // All Olympia-era EIPs (ECIP-1121) activate at the same block as OlympiaTransition.
        // Propagating here means only olympiaTransition needs updating in the chainspec JSON
        // when the activation block is finalized — individual eip*Transition entries are
        // intentionally omitted from the params section of each chainspec.
        if (OlympiaTransition is not null)
        {
            long olympia = OlympiaTransition.Value;
            chainSpec.Parameters.Eip1559Transition = olympia;
            chainSpec.Parameters.Eip3198Transition = olympia;
            chainSpec.Parameters.Eip5656Transition = olympia;
            chainSpec.Parameters.Eip1153Transition = olympia;
            chainSpec.Parameters.Eip6780Transition = olympia;
            chainSpec.Parameters.Eip2537Transition = olympia;
            chainSpec.Parameters.Eip7823Transition = olympia;
            chainSpec.Parameters.Eip7883Transition = olympia;
            chainSpec.Parameters.Eip7825Transition = olympia;
            chainSpec.Parameters.Eip7623Transition = olympia;
            chainSpec.Parameters.Eip7951Transition = olympia;
            chainSpec.Parameters.Eip2935Transition = olympia;
            chainSpec.Parameters.Eip7702Transition = olympia;
            chainSpec.Parameters.Eip7934Transition = olympia;
        }
    }

    void IChainSpecEngineParameters.ApplyToReleaseSpec(ReleaseSpec spec, long startBlock, ulong? startTimestamp)
    {
        // Call base implementation (sets BlockReward, DifficultyBombDelay, Eip2/7/100, DifficultyBoundDivisor)
        base.ApplyToReleaseSpec(spec, startBlock, startTimestamp);

        // ETC difficulty bomb is handled by EtchashDifficultyCalculator with hardcoded logic.
        // DifficultyBombDelays in chainspec are only used for Fork ID, not actual difficulty.
        spec.DifficultyBombDelay = 0;

        // EIP-1559 (ECIP-1111) and EIP-3198 (BASEFEE opcode) activate at Olympia, not earlier.
        // Suppress them until OlympiaTransition and hold ElasticityMultiplier at 1 so that
        // Eip1559GasLimitAdjuster does not double the gas limit at any pre-Olympia fork block.
        if (OlympiaTransition is not null && startBlock < OlympiaTransition.Value)
        {
            spec.IsEip1559Enabled = false;
            spec.IsEip3198Enabled = false;
            spec.ElasticityMultiplier = 1;
        }

        // Suppress the one-shot 2× gas limit doubling that Eip1559GasLimitAdjuster applies
        // at Eip1559TransitionBlock. ETC Olympia drives 8M→60M via 1/1024-per-block convergence
        // (OlympiaGasLimitCalculator), not ETH London's one-time doubling. Setting this to
        // long.MaxValue ensures the (Eip1559TransitionBlock == blockNumber) check never fires.
        if (OlympiaTransition is not null && startBlock >= OlympiaTransition.Value)
        {
            spec.Eip1559TransitionBlock = long.MaxValue;
        }
    }
}

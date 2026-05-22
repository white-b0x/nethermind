// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using Nethermind.Consensus;
using Nethermind.Core;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Fork-scheduled gas limit calculator for the Olympia hardfork (ECIP-1121 / EIP-7935).
/// </summary>
/// <remarks>
/// Pre-Olympia: returns the parent gas limit unchanged, preserving ETC's 8M limit through Spiral.
/// Post-Olympia: drives the gas limit toward 60M via the standard ±(parentGasLimit / 1024 - 1)
/// per-block adjustment defined in the Yellow Paper. This automatic targeting prevents operators
/// from inadvertently keeping the 8M limit after Olympia activation.
///
/// Convergence from 8M: delta ≈ 7,812 gas/block → ~6,660 blocks to reach 60M.
/// </remarks>
public class OlympiaGasLimitCalculator(long? olympiaTransition) : IGasLimitCalculator
{
    private const long OlympiaGasTarget = 60_000_000;
    private const long GasLimitBoundDivisor = 1024;

    public long GetGasLimit(BlockHeader parentHeader)
    {
        if (olympiaTransition is null || parentHeader.Number + 1 < olympiaTransition.Value)
            return parentHeader.GasLimit;

        long parent = parentHeader.GasLimit;
        long delta = Math.Max(parent / GasLimitBoundDivisor - 1, 1);

        if (parent < OlympiaGasTarget)
            return Math.Min(parent + delta, OlympiaGasTarget);
        if (parent > OlympiaGasTarget)
            return Math.Max(parent - delta, OlympiaGasTarget);
        return parent;
    }
}

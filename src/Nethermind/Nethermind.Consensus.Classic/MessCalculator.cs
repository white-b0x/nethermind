// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Int256;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// MESS (Modified Exponential Subjective Scoring) calculator.
/// Implements the polynomial antigravity function from ECBP-1100.
/// Port of core-geth/core/blockchain_af.go.
/// </summary>
internal static class MessCalculator
{
    private static readonly UInt256 Denominator = 128;
    private static readonly UInt256 XCap = 25132;
    private static readonly UInt256 Height = 3840;

    /// <summary>
    /// Computes the antigravity polynomial value for a given time delta in seconds.
    /// Returns a value from 128 (at timeDelta=0) to 3968 (at timeDelta>=25132).
    /// </summary>
    public static UInt256 PolynomialV(ulong timeDeltaSeconds)
    {
        UInt256 x = timeDeltaSeconds;
        if (x > XCap)
            x = XCap;

        UInt256 x2 = x * x;
        UInt256 term1 = 3 * x2;

        UInt256 x3 = x2 * x;
        UInt256 term2 = 2 * x3 / XCap;

        UInt256 xcap2 = XCap * XCap;
        UInt256 result = (term1 - term2) * Height / xcap2;

        return Denominator + result;
    }

    /// <summary>
    /// Determines whether a reorg should be rejected by MESS.
    /// </summary>
    public static bool ShouldRejectReorg(
        UInt256 commonAncestorTD,
        UInt256 localTD,
        UInt256 proposedTD,
        ulong commonAncestorTime,
        ulong currentHeadTime)
    {
        UInt256 proposedSubchainTD = proposedTD - commonAncestorTD;
        UInt256 localSubchainTD = localTD - commonAncestorTD;

        ulong timeDelta = currentHeadTime >= commonAncestorTime
            ? currentHeadTime - commonAncestorTime
            : 0;

        UInt256 antigravity = PolynomialV(timeDelta);

        return proposedSubchainTD * Denominator < antigravity * localSubchainTD;
    }
}

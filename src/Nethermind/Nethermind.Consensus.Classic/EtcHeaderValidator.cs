// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Blockchain;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Logging;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// ETC header validator that extends the base HeaderValidator with ECIP-1122 gas limit target warnings.
/// </summary>
/// <remarks>
/// Overrides <see cref="ValidateGasLimitRange"/> to emit a WARN-level log when a peer block's
/// gasLimit is below the network-scheduled gas limit target for its epoch.  The block is still
/// accepted — this is a SHOULD requirement per ECIP-1122, not a MUST.
///
/// <list type="bullet">
///   <item>Pre-Olympia (Spiral epoch): gas limit target = 8,000,000</item>
///   <item>Olympia epoch and later: gas limit target = 60,000,000</item>
/// </list>
///
/// Mirrors the <c>ForkGasTarget</c> check in core-geth's <c>VerifyEIP1559Header</c>.
/// </remarks>
public class EtcHeaderValidator(
    IBlockTree blockTree,
    ISealValidator sealValidator,
    ISpecProvider specProvider,
    ILogManager logManager,
    long? olympiaTransition)
    : HeaderValidator(blockTree, sealValidator, specProvider, logManager)
{
    private const long SpiralGasLimit = 8_000_000L;
    private const long OlympiaGasLimit = 60_000_000L;

    protected override bool ValidateGasLimitRange(
        BlockHeader header, BlockHeader parent, IReleaseSpec spec, ref string? error)
    {
        bool result = base.ValidateGasLimitRange(header, parent, spec, ref error);

        // ECIP-1122 SHOULD: warn (not reject) when a peer mines below the network gas limit target.
        long limit = SelectGasLimit(header.Number, olympiaTransition);
        if (header.GasLimit < limit)
        {
            if (_logger.IsWarn)
                _logger.Warn(
                    $"Peer block gas limit below network gas limit target (ECIP-1122 SHOULD): " +
                    $"block={header.Number}, gasLimit={header.GasLimit}, target={limit}");
        }

        return result;
    }

    /// <summary>
    /// Returns the ECIP-1122 gas limit target for the given block number.
    /// Olympia epoch: 60 M; pre-Olympia (Spiral): 8 M.
    /// </summary>
    internal static long SelectGasLimit(long blockNumber, long? olympiaTransition) =>
        olympiaTransition is not null && blockNumber >= olympiaTransition.Value
            ? OlympiaGasLimit
            : SpiralGasLimit;
}

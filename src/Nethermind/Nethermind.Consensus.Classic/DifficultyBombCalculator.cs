// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Pure calculation methods for Ethereum Classic difficulty bomb.
/// </summary>
public static class DifficultyBombCalculator
{
    public const long InitialBombBlock = 200_000;
    public const long ExponentialPeriod = 100_000;

    /// <summary>
    /// Calculates the difficulty bomb contribution for a given block.
    /// </summary>
    /// <param name="blockNumber">The block number.</param>
    /// <param name="dieHardBlock">DieHard fork block (bomb paused), or null if never existed.</param>
    /// <param name="gothamBlock">Gotham fork block (bomb delayed), or null if not applicable.</param>
    /// <param name="ecip1041Block">ECIP-1041 block (bomb removed), or null if not applicable.</param>
    public static BigInteger CalculateTimeBomb(
        long blockNumber,
        long? dieHardBlock,
        long? gothamBlock,
        long? ecip1041Block)
    {
        if (ecip1041Block is not null && blockNumber >= ecip1041Block)
            return BigInteger.Zero;

        if (dieHardBlock is null)
            return BigInteger.Zero;

        long period = blockNumber / ExponentialPeriod;

        if (gothamBlock is not null && blockNumber >= gothamBlock)
        {
            long bombDelay = (gothamBlock.Value - dieHardBlock.Value) / ExponentialPeriod;
            return period - bombDelay - 2 < 0
                ? BigInteger.Zero
                : BigInteger.Pow(2, (int)(period - bombDelay - 2));
        }

        if (blockNumber >= dieHardBlock)
        {
            long fixedPeriod = dieHardBlock.Value / ExponentialPeriod;
            return BigInteger.Pow(2, (int)(fixedPeriod - 2));
        }

        if (blockNumber < InitialBombBlock)
            return BigInteger.Zero;

        return period < 2 ? BigInteger.Zero : BigInteger.Pow(2, (int)(period - 2));
    }
}

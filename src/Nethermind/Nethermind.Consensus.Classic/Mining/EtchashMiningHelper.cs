// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Numerics;

namespace Nethermind.Consensus.Classic.Mining;

internal static class EtchashMiningHelper
{
    internal const long EpochLength = 30000;
    internal const long EtchashEpochLength = 60000;

    internal static readonly BigInteger TwoTo256 = BigInteger.Pow(2, 256);

    internal static uint GetEtchashEpoch(long blockNumber, long ecip1099Transition, uint transitionEpoch) =>
        blockNumber < ecip1099Transition
            ? (uint)(blockNumber / EpochLength)
            : (transitionEpoch / 2) + (uint)((blockNumber - ecip1099Transition) / EtchashEpochLength);

    internal static uint GetSeedEpoch(uint dagEpoch, bool ecip1099Active) =>
        ecip1099Active ? dagEpoch * 2 : dagEpoch;

    internal static byte[] ComputeTargetBytes(in BigInteger difficulty)
    {
        BigInteger target = TwoTo256 / difficulty;

        byte[] targetBytes = new byte[32];
        byte[] rawBytes = target.ToByteArray(isUnsigned: true, isBigEndian: true);

        int offset = 32 - rawBytes.Length;
        if (offset >= 0)
        {
            Array.Copy(rawBytes, 0, targetBytes, offset, rawBytes.Length);
        }
        else
        {
            Array.Fill(targetBytes, (byte)0xFF);
        }

        return targetBytes;
    }
}

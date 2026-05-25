// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Numerics;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Crypto;
using Nethermind.Int256;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Base Ethash implementation containing the core PoW algorithm constants and methods.
/// Standalone copy — Nethermind.Consensus.Ethash types are internal.
/// </summary>
internal static class EthashBase
{
    public const int WordBytes = 4;
    public static readonly uint DataSetBytesInit = 1U << 30;
    public static readonly uint DataSetBytesGrowth = 1U << 23;
    public static readonly uint CacheBytesInit = 1U << 24;
    public static readonly uint CacheBytesGrowth = 1U << 17;
    public const int CacheMultiplier = 1024;
    public const long EpochLength = 30000;
    public const uint MixBytes = 128;
    public const int HashBytes = 64;
    public const uint DataSetParents = 256;
    public const int CacheRounds = 3;
    public const int Accesses = 64;
    internal const uint FnvPrime = 0x01000193;

    private static readonly BigInteger TwoTo256 = BigInteger.Pow(2, 256);

    public static uint GetEpoch(long blockNumber) => (uint)(blockNumber / EpochLength);

    public static ulong GetDataSize(uint epoch)
    {
        uint upperBound = (DataSetBytesInit / MixBytes) + (DataSetBytesGrowth / MixBytes) * epoch;
        uint dataItems = FindLargestPrime(upperBound);
        return dataItems * (ulong)MixBytes;
    }

    public static uint GetCacheSize(uint epoch)
    {
        uint upperBound = (CacheBytesInit / HashBytes) + (CacheBytesGrowth / HashBytes) * epoch;
        uint cacheItems = FindLargestPrime(upperBound);
        return cacheItems * HashBytes;
    }

    public static uint FindLargestPrime(uint upper)
    {
        if (upper < 2U) throw new ArgumentException("There are no prime numbers below 2");
        if (upper == 2U) return upper;

        uint number = (upper % 2 == 0 ? upper - 1 : upper);

        for (; number > 5; number -= 2)
        {
            if (IsPrime(number)) return number;
        }

        return number;
    }

    public static bool IsPrime(uint number)
    {
        if (number <= 1U) return false;
        if (number == 2U) return true;
        if (number % 2U == 0U) return false;

        for (uint d = 3; d * (ulong)d <= number; d += 2)
        {
            if (number % d == 0)
                return false;
        }

        return true;
    }

    public static Hash256 GetSeedHash(uint epoch)
    {
        ValueHash256 seed = new();
        for (uint i = 0; i < epoch; i++)
        {
            seed = ValueKeccak.Compute(seed.Bytes);
        }

        return new Hash256(seed.Bytes);
    }

    internal static void Fnv(Span<uint> b1, Span<uint> b2)
    {
        for (int i = 0; i < b1.Length; i++)
        {
            b1[i] = Fnv(b1[i], b2[i]);
        }
    }

    internal static uint Fnv(uint v1, uint v2) => (v1 * FnvPrime) ^ v2;

    private static uint GetUInt(byte[] bytes, uint offset) => BitConverter.ToUInt32(BitConverter.IsLittleEndian ? bytes : Bytes.Reverse(bytes), (int)offset * 4);

    public static bool IsLessOrEqualThanTarget(ReadOnlySpan<byte> result, in UInt256 difficulty)
    {
        UInt256 resultAsInteger = new(result, true);
        BigInteger target = BigInteger.Divide(TwoTo256, (BigInteger)difficulty);
        return (BigInteger)resultAsInteger <= target;
    }

    internal static bool IsValidPoWResult(bool mixHashMatches, ReadOnlySpan<byte> result, in UInt256 difficulty) =>
        mixHashMatches && IsLessOrEqualThanTarget(result, difficulty);

    internal static bool IsValidMiningResult(byte[]? mixHash, bool hashimotoSuccess, ReadOnlySpan<byte> result, in UInt256 difficulty) =>
        mixHash is not null && IsValidPoWResult(hashimotoSuccess, result, difficulty);

    internal static (byte[]?, ValueHash256, bool) Hashimoto(ulong fullSize, IEthashDataSet dataSet, Hash256 headerHash, Hash256? expectedMixHash, ulong nonce)
    {
        uint hashesInFull = (uint)(fullSize / HashBytes);
        const uint wordsInMix = MixBytes / WordBytes;
        const uint hashesInMix = MixBytes / HashBytes;

        byte[] nonceBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(nonceBytes, nonce);

        byte[] headerAndNonceHashed = Keccak512.Compute(Bytes.Concat(headerHash.BytesToArray(), nonceBytes)).Bytes;
        uint[] mixInts = new uint[MixBytes / WordBytes];

        for (int i = 0; i < hashesInMix; i++)
        {
            Buffer.BlockCopy(headerAndNonceHashed, 0, mixInts, i * headerAndNonceHashed.Length, headerAndNonceHashed.Length);
        }

        uint firstOfHeaderAndNonce = GetUInt(headerAndNonceHashed, 0);
        for (uint i = 0; i < Accesses; i++)
        {
            uint p = Fnv(i ^ firstOfHeaderAndNonce, mixInts[i % wordsInMix]) % (hashesInFull / hashesInMix) * hashesInMix;
            uint[] newData = new uint[wordsInMix];
            for (uint j = 0; j < hashesInMix; j++)
            {
                uint[] item = dataSet.CalcDataSetItem(p + j);
                Buffer.BlockCopy(item, 0, newData, (int)(j * item.Length * 4), item.Length * 4);
            }

            Fnv(mixInts, newData);
        }

        uint[] cmixInts = new uint[MixBytes / WordBytes / 4];
        for (uint i = 0; i < mixInts.Length; i += 4)
        {
            cmixInts[i / 4] = Fnv(Fnv(Fnv(mixInts[i], mixInts[i + 1]), mixInts[i + 2]), mixInts[i + 3]);
        }

        byte[] cmix = new byte[MixBytes / WordBytes];
        Buffer.BlockCopy(cmixInts, 0, cmix, 0, cmix.Length);

        if (expectedMixHash is not null && !Bytes.AreEqual(cmix, expectedMixHash.Bytes))
        {
            return (null, default, false);
        }

        return (cmix, ValueKeccak.Compute(Bytes.Concat(headerAndNonceHashed, cmix)), true);
    }
}

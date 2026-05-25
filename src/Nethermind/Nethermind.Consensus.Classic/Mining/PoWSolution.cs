// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;

namespace Nethermind.Consensus.Classic.Mining;

public sealed record PoWSolution(
    ulong Nonce,
    Hash256 PowHash,
    Hash256 MixHash);

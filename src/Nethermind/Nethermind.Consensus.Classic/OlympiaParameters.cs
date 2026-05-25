// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using Nethermind.Core;

namespace Nethermind.Consensus.Classic;

/// <summary>ECIP-1112: Olympia treasury activation block and recipient address.</summary>
internal sealed record OlympiaParameters(long Transition, Address TreasuryAddress);

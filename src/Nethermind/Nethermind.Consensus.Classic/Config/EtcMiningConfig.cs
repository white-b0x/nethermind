// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Consensus.Classic.Config;

public class EtcMiningConfig : IEtcMiningConfig
{
    public EtcMiningMode Mode { get; set; } = EtcMiningMode.None;
}

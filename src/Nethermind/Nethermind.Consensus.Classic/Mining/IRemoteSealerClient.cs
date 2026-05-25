// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;

namespace Nethermind.Consensus.Classic.Mining;

public interface IRemoteSealerClient
{
    MiningWork? GetWork();
    bool SubmitWork(PoWSolution solution);
    void SubmitNewWork(Block block);
    void SetOnBlockMined(Action<Block> callback);
}

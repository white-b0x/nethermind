// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.JsonRpc;
using Nethermind.JsonRpc.Modules;

namespace Nethermind.Consensus.Classic.Mining;

[RpcModule(ModuleType.Eth)]
public interface IEtcMiningRpcModule : IRpcModule
{
    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Returns mining work for external miners.",
        ExampleResponse = "[\"0x1234...\",\"0x5678...\",\"0xabcd...\",\"0x100\"]")]
    ResultWrapper<string[]> eth_getWork();

    [JsonRpcMethod(
        IsImplemented = true,
        Description = "Submits a mining solution.",
        ExampleResponse = "true")]
    ResultWrapper<bool> eth_submitWork(byte[] nonce, byte[] powHash, byte[] mixDigest);
}

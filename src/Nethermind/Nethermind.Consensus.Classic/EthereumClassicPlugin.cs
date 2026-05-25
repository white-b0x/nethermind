// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Consensus.Classic.Config;
using Nethermind.Consensus.Classic.Mining;
using Nethermind.Consensus.Ethash;
using Nethermind.Consensus.Rewards;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.JsonRpc.Modules;
using Nethermind.KeyStore.Config;
using Nethermind.Logging;
using Nethermind.Network.Config;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.Synchronization.Peers;

namespace Nethermind.Consensus.Classic;

/// <summary>
/// Consensus plugin for Ethereum Classic Etchash.
/// Sets SealEngineType to "Etchash" to disable EthashPlugin and act as the sole consensus plugin.
/// </summary>
public class EthereumClassicPlugin(
    ChainSpec chainSpec,
    IEtcMiningConfig miningConfig,
    IEtcMessConfig messConfig,
    IKeyStoreConfig keyStoreConfig) : IConsensusPlugin
{
    public string Name => "Etchash";
    public string Description => "Ethereum Classic Etchash Consensus (ECIP-1099)";
    public string Author => "Ethereum Classic Community";

    public string SealEngineType => "Etchash";

    private INethermindApi? _nethermindApi;
    private MessActivationMonitor? _messMonitor;

    private EtchashChainSpecEngineParameters? GetEtchashParams() =>
        chainSpec.EngineChainSpecParametersProvider?.AllChainSpecParameters
            .OfType<EtchashChainSpecEngineParameters>().FirstOrDefault();

    public bool Enabled => GetEtchashParams() is not null;

    public Task Init(INethermindApi api)
    {
        _nethermindApi = api;

        BlocksConfig.GasTokenTicker = "ETC";
        string numericVersion = ProductInfo.Version.Split('-', '+')[0];
        api.Config<INetworkConfig>().PublicClientIdFormat =
            $"{{name}}/Ethereum Classic/{numericVersion}/{{os}}/{{runtime}}";

        if (miningConfig.Mode != EtcMiningMode.None)
        {
            if (string.IsNullOrWhiteSpace(keyStoreConfig.BlockAuthorAccount))
            {
                throw new InvalidOperationException(
                    $"KeyStore.BlockAuthorAccount is required when EtcMining.Mode is {miningConfig.Mode}");
            }
        }

        return Task.CompletedTask;
    }

    public Task InitNetworkProtocol()
    {
        if (messConfig.Enabled)
        {
            EtcBlockTree? blockTree = _nethermindApi!.BlockTree as EtcBlockTree;
            if (blockTree is not null)
            {
                _messMonitor = new MessActivationMonitor(
                    blockTree,
                    _nethermindApi.Context.Resolve<ISyncPeerPool>(),
                    _nethermindApi.Timestamper,
                    _nethermindApi.LogManager);
                _messMonitor.Start();
            }
        }

        return Task.CompletedTask;
    }

    public Task InitRpcModules() => Task.CompletedTask;

    public IBlockProducer InitBlockProducer()
    {
        (IApiWithBlockchain getFromApi, IApiWithBlockchain _) = _nethermindApi!.ForProducer;

        IBlockProducerEnv env = getFromApi.BlockProducerEnvFactory.CreatePersistent();
        return new EtchashBlockProducer(
            env.TxSource,
            env.ChainProcessor,
            env.ReadOnlyStateProvider,
            getFromApi.BlockTree,
            getFromApi.Timestamper,
            getFromApi.SpecProvider,
            getFromApi.Config<IBlocksConfig>(),
            _nethermindApi.Context.Resolve<ISealer>(),
            _nethermindApi.Context.Resolve<IDifficultyCalculator>(),
            getFromApi.LogManager);
    }

    public IBlockProducerRunner InitBlockProducerRunner(IBlockProducer blockProducer) => new StandardBlockProducerRunner(
            _nethermindApi!.ManualBlockProductionTrigger,
            _nethermindApi.BlockTree,
            blockProducer);

    public IModule? Module
    {
        get
        {
            EtchashChainSpecEngineParameters p = GetEtchashParams();
            if (p is null) return null;

            if (p.Ecip1099Transition is null)
                throw new InvalidOperationException("ecip1099Transition is required for Etchash chains");
            if (p.Ecip1017EraRounds <= 0)
                throw new InvalidOperationException("ecip1017EraRounds is required for Etchash chains");

            return new EthereumClassicModule(
                p.Ecip1099Transition.Value,
                p.Ecip1017EraRounds,
                p.DieHardTransition,
                p.GothamTransition,
                p.Ecip1041Transition,
                p.OlympiaTransition,
                p.OlympiaTreasuryAddress,
                miningConfig.Mode,
                messConfig.Enabled);
        }
    }
}

public class EthereumClassicModule(
    long ecip1099Transition,
    long ecip1017EraRounds,
    long? dieHardTransition,
    long? gothamTransition,
    long? ecip1041Transition,
    long? olympiaTransition,
    Address? olympiaTreasuryAddress,
    EtcMiningMode miningMode,
    bool messEnabled) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        if (messEnabled)
        {
            builder.RegisterType<EtcBlockTree>()
                .As<IBlockTree>()
                .AsSelf()
                .SingleInstance();
        }

        builder.Register(ctx => new Etchash(ctx.Resolve<ILogManager>(), ecip1099Transition))
            .As<IEthash>()
            .SingleInstance();

        builder.Register(ctx => new EtchashDifficultyCalculator(
                ctx.Resolve<ISpecProvider>(),
                dieHardTransition,
                gothamTransition,
                ecip1041Transition))
            .As<IDifficultyCalculator>()
            .SingleInstance();

        OlympiaParameters? olympiaParams = olympiaTransition is not null && olympiaTreasuryAddress is not null
            ? new OlympiaParameters(olympiaTransition.Value, olympiaTreasuryAddress)
            : null;

        builder.Register(_ => new EtcRewardCalculator(ecip1017EraRounds, olympiaParams))
            .As<IRewardCalculatorSource>()
            .SingleInstance();

        builder.Register(ctx => new EthashSealValidator(
                ctx.Resolve<ILogManager>(),
                ctx.Resolve<IDifficultyCalculator>(),
                ctx.Resolve<ICryptoRandom>(),
                ctx.Resolve<IEthash>(),
                ctx.Resolve<ITimestamper>()))
            .As<ISealValidator>()
            .SingleInstance();

        if (miningMode == EtcMiningMode.Remote)
        {
            builder.Register(ctx => new RemoteSealerClient(
                    ctx.Resolve<IEthash>(),
                    ecip1099Transition,
                    ctx.Resolve<ILogManager>()))
                .As<IRemoteSealerClient>()
                .SingleInstance();

            builder.RegisterSingletonJsonRpcModule<IEtcMiningRpcModule, EtcMiningRpcModule>();

            builder.Register(ctx => new RemoteEtchashSealer(
                    ctx.Resolve<IRemoteSealerClient>(),
                    ctx.Resolve<ISigner>(),
                    ctx.Resolve<ILogManager>()))
                .As<ISealer>()
                .SingleInstance();
        }
        else if (miningMode == EtcMiningMode.Local)
        {
            builder.Register(ctx => new LocalEtchashSealer(
                    ctx.Resolve<IEthash>(),
                    ctx.Resolve<ISigner>(),
                    ctx.Resolve<ILogManager>()))
                .As<ISealer>()
                .SingleInstance();
        }
    }
}

// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Net;
using System.Runtime.CompilerServices;
using DnsClient;
using DotNetty.Buffers;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Network.Config;
using Nethermind.Network.Enr;
using Nethermind.Serialization.Rlp;
using Nethermind.Stats.Model;

namespace Nethermind.Network.Dns;

public class EnrDiscovery : INodeSource
{
    private readonly IEnrRecordParser _parser;
    private readonly ILogger _logger;
    private readonly EnrTreeCrawler _crawler;
    private readonly string[] _domains;

    public EnrDiscovery(IEnrRecordParser parser, INetworkConfig networkConfig, ILogManager logManager)
    {
        _parser = parser;
        _logger = logManager.GetClassLogger<EnrDiscovery>();
        _crawler = new EnrTreeCrawler(_logger);
        // Supports comma-separated list of DNS discovery URLs for fallback redundancy.
        // Each domain is crawled in order; a DNS error on one does not abort the others.
        _domains = (networkConfig.DiscoveryDns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async IAsyncEnumerable<Node> DiscoverNodes([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string domain in _domains)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            IByteBuffer buffer = NethermindBuffers.Default.Buffer();
            await using ConfiguredCancelableAsyncEnumerable<string>.Enumerator enumerator = _crawler.SearchTree(domain, cancellationToken)
                .WithCancellation(cancellationToken)
                .GetAsyncEnumerator();

            try
            {
                // Need to loop manually because of the exception handling
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool hasNext = false;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (DnsResponseException dnsException)
                    {
                        if (_logger.IsWarn) _logger.Warn($"Searching the tree of \"{domain}\" had an error: {dnsException.DnsError}");
                        break; // continue to next domain
                    }

                    if (!hasNext) break;

                    string nodeRecordText = enumerator.Current;
                    Node? node = null;
                    try
                    {
                        NodeRecord nodeRecord = _parser.ParseRecord(nodeRecordText, buffer);
                        node = CreateNode(nodeRecord);
                    }
                    catch (Exception e)
                    {
                        _logger.DebugError($"failed to parse enr record {nodeRecordText}", e);
                    }

                    if (node is not null)
                    {
                        // here could add network info to the node
                        yield return node;
                    }
                }
            }
            finally
            {
                buffer.Release();
            }
        }
    }

    private static Node? CreateNode(NodeRecord nodeRecord)
    {
        CompressedPublicKey? compressedPublicKey = nodeRecord.GetObj<CompressedPublicKey>(EnrContentKey.SecP256k1);
        IPAddress? ipAddress = nodeRecord.GetObj<IPAddress>(EnrContentKey.Ip);
        int? port = nodeRecord.GetValue<int>(EnrContentKey.Tcp) ?? nodeRecord.GetValue<int>(EnrContentKey.Udp);
        return compressedPublicKey is not null && ipAddress is not null && port is not null
            ? new(compressedPublicKey.Decompress(), ipAddress.ToString(), port.Value)
            : null;
    }

    public event EventHandler<NodeEventArgs>? NodeRemoved { add { } remove { } }
}

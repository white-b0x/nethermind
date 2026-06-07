// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Logging;
using Open.Nat;

namespace Nethermind.Network.IP;

/// <summary>Detects external IP via UPnP IGD GetExternalIPAddress.</summary>
/// <remarks>
/// Uses <c>Open.NAT.Core</c> (the same library as <c>Nethermind.UPnP.Plugin</c>) to discover
/// the local gateway and query its WAN address. Returns immediately on VPS or firewalled
/// environments where no UPnP device responds within the timeout. Silent on failure — caller
/// falls through to the next <see cref="IIPSource"/> in the cascade.
/// </remarks>
class UPnPIPSource(ILogManager logManager) : IIPSource
{
    private const int TimeoutMs = 3000;
    private readonly ILogger _logger = logManager.GetClassLogger<UPnPIPSource>();

    public async Task<(bool Success, IPAddress Ip)> TryGetIP()
    {
        try
        {
            using CancellationTokenSource cts = new(TimeoutMs);
            NatDiscoverer discoverer = new();
            NatDevice device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            IPAddress externalIp = await device.GetExternalIPAsync();

            if (externalIp is not null && !externalIp.IsInternal())
            {
                if (_logger.IsDebug) _logger.Debug($"UPnP IGD returned external IP: {externalIp}");
                return (true, externalIp);
            }
        }
        catch (NatDeviceNotFoundException)
        {
            if (_logger.IsDebug) _logger.Debug("No UPnP gateway found — skipping UPnP IP detection");
        }
        catch (Exception e)
        {
            if (_logger.IsDebug) _logger.Debug($"UPnP IP detection failed: {e.Message}");
        }

        return (false, null!);
    }
}

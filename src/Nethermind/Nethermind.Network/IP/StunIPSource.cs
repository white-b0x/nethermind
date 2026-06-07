// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Nethermind.Logging;

namespace Nethermind.Network.IP;

/// <summary>Detects external IP via RFC 5389 STUN Binding Request.</summary>
/// <remarks>
/// Parses the XOR-MAPPED-ADDRESS attribute (type 0x0020) to determine the NAT-mapped public
/// address. Probes the same canonical STUN servers used by go-ethereum and Besu's
/// <c>StunIpDetector</c>, returning on the first successful response.
/// </remarks>
class StunIPSource(ILogManager logManager) : IIPSource
{
    private static readonly (string Host, int Port)[] _servers =
    [
        ("stun.l.google.com",  19302),
        ("stun1.l.google.com", 19302),
        ("stun.cloudflare.com", 3478),
    ];

    private const int TimeoutMs = 3000;
    private readonly ILogger _logger = logManager.GetClassLogger<StunIPSource>();

    public Task<(bool Success, IPAddress Ip)> TryGetIP()
    {
        foreach ((string host, int port) in _servers)
        {
            try
            {
                IPAddress? ip = StunProbe(host, port);
                if (ip is not null && !ip.IsInternal())
                {
                    if (_logger.IsDebug) _logger.Debug($"STUN {host}:{port} returned external IP: {ip}");
                    return Task.FromResult<(bool, IPAddress)>((true, ip));
                }
            }
            catch (Exception e)
            {
                if (_logger.IsDebug) _logger.Debug($"STUN probe to {host}:{port} failed: {e.Message}");
            }
        }

        return Task.FromResult<(bool, IPAddress)>((false, null!));
    }

    private static IPAddress? StunProbe(string host, int port)
    {
        using UdpClient udp = new();
        udp.Client.ReceiveTimeout = TimeoutMs;
        udp.Client.SendTimeout = TimeoutMs;

        // RFC 5389 §6: Binding Request — 20-byte fixed header, zero body attributes
        byte[] req = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0), 0x0001); // Message Type: Binding Request
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2), 0x0000); // Message Length: 0 (no body)
        BinaryPrimitives.WriteUInt32BigEndian(req.AsSpan(4), 0x2112_A442); // Magic Cookie (RFC 5389)
        // Bytes 8–19: Transaction ID — all zeros (we accept any response)

        udp.Send(req, req.Length, host, port);

        IPEndPoint remote = new(IPAddress.Any, 0);
        byte[] resp = udp.Receive(ref remote);
        return ParseXorMappedAddress(resp);
    }

    /// <summary>
    /// Parses the XOR-MAPPED-ADDRESS attribute (0x0020) from a STUN Binding Success Response.
    /// </summary>
    /// <remarks>
    /// Returns null if the response is not a Binding Success Response (type 0x0101), if the
    /// XOR-MAPPED-ADDRESS attribute is absent, or if the address family is not IPv4 (0x01).
    ///
    /// XOR-MAPPED-ADDRESS attribute body layout (RFC 5389 §15.2):
    ///   1 byte  reserved | 1 byte family | 2 bytes XOR-port | 4 bytes XOR-address
    ///   IPv4 address = XOR-address field XOR magic cookie (0x2112A442)
    /// </remarks>
    private static IPAddress? ParseXorMappedAddress(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 20) return null;

        if (BinaryPrimitives.ReadUInt16BigEndian(buf) != 0x0101) return null; // not a success response

        int msgLen = BinaryPrimitives.ReadUInt16BigEndian(buf[2..]);
        int pos = 20; // attribute section begins after 20-byte header
        int bodyEnd = 20 + msgLen;

        while (pos + 4 <= bodyEnd && pos + 4 <= buf.Length)
        {
            ushort attrType = BinaryPrimitives.ReadUInt16BigEndian(buf[pos..]);
            int attrLen = BinaryPrimitives.ReadUInt16BigEndian(buf[(pos + 2)..]);
            int valStart = pos + 4;

            if (attrType == 0x0020 && attrLen >= 8 && valStart + 8 <= buf.Length)
            {
                // XOR-MAPPED-ADDRESS body: reserved(1) + family(1) + xport(2) + xaddr(4)
                byte family = buf[valStart + 1];
                if (family == 0x01) // IPv4
                {
                    uint xorAddr = BinaryPrimitives.ReadUInt32BigEndian(buf[(valStart + 4)..]);
                    uint addr = xorAddr ^ 0x2112_A442U;
                    byte[] bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, addr);
                    return new IPAddress(bytes);
                }
            }

            pos = valStart + ((attrLen + 3) & ~3); // advance past padded attribute
        }

        return null;
    }
}

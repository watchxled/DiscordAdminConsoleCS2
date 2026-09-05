using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace DiscordAdminConsole.Monitoring;

public sealed class A2SInfo
{
    public required string Name { get; init; }
    public required string Map { get; init; }
    public required int Players { get; init; }
    public required int MaxPlayers { get; init; }
    public required int Bots { get; init; }
    public required int PingMs { get; init; }
}

public static class A2SQuery
{
    public static async Task<A2SInfo?> QueryAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var udp = new UdpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs)));
            udp.Connect(host, port);

            var challenge = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            var started = Stopwatch.StartNew();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var request = BuildRequest(challenge);
                await udp.SendAsync(request, cts.Token);

                while (true)
                {
                    var result = await udp.ReceiveAsync(cts.Token);
                    var data = result.Buffer;
                    if (data.Length < 5)
                        continue;

                    var payload = StripHeader(data, out var header);
                    if (payload == null)
                        continue;

                    if (header == 0x41)
                    {
                        challenge = payload.Length >= 4 ? payload[..4] : challenge;
                        break;
                    }

                    if (header == 0x49)
                        return ParseSource(payload, started.ElapsedMilliseconds);

                    if (header == 0x6D)
                        return ParseGoldSrc(payload, started.ElapsedMilliseconds);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildRequest(byte[] challenge)
    {
        const string payload = "Source Engine Query";
        var packet = new byte[4 + 1 + payload.Length + 1 + 4];
        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0xFF;
        packet[3] = 0xFF;
        packet[4] = 0x54;
        Encoding.ASCII.GetBytes(payload, 0, payload.Length, packet, 5);
        packet[5 + payload.Length] = 0;
        Buffer.BlockCopy(challenge, 0, packet, 6 + payload.Length, 4);
        return packet;
    }

    private static byte[]? StripHeader(byte[] data, out int header)
    {
        header = -1;
        if (data.Length >= 5 &&
            data[0] == 0xFF && data[1] == 0xFF && data[2] == 0xFF && data[3] == 0xFF)
        {
            header = data[4];
            return data[5..];
        }

        if (data.Length >= 6 && data[0] == 0xFE)
        {
            header = 0xFE;
            return null;
        }

        return null;
    }

    private static A2SInfo? ParseSource(byte[] b, long pingMs)
    {
        try
        {
            var r = new Reader(b);
            r.Skip(1);
            var name = r.ReadString();
            var map = r.ReadString();
            r.ReadString();
            r.ReadString();
            r.Skip(2);
            var players = r.ReadByte();
            var max = r.ReadByte();
            var bots = r.ReadByte();
            return new A2SInfo
            {
                Name = name,
                Map = string.IsNullOrEmpty(map) ? "-" : map,
                Players = players,
                MaxPlayers = max,
                Bots = bots,
                PingMs = (int)pingMs,
            };
        }
        catch
        {
            return null;
        }
    }

    private static A2SInfo? ParseGoldSrc(byte[] b, long pingMs)
    {
        try
        {
            var r = new Reader(b);
            r.ReadString();
            var name = r.ReadString();
            var map = r.ReadString();
            r.ReadString();
            r.ReadString();
            var players = r.ReadByte();
            var max = r.ReadByte();
            return new A2SInfo
            {
                Name = name,
                Map = string.IsNullOrEmpty(map) ? "-" : map,
                Players = players,
                MaxPlayers = max,
                Bots = 0,
                PingMs = (int)pingMs,
            };
        }
        catch
        {
            return null;
        }
    }

    private ref struct Reader
    {
        private readonly byte[] _data;
        private int _pos;

        public Reader(byte[] data)
        {
            _data = data;
            _pos = 0;
        }

        public void Skip(int count) => _pos += count;

        public byte ReadByte()
        {
            if (_pos >= _data.Length)
                throw new EndOfStreamException();
            return _data[_pos++];
        }

        public string ReadString()
        {
            var end = Array.IndexOf(_data, (byte)0, _pos);
            if (end < 0)
                throw new EndOfStreamException();
            var s = Encoding.UTF8.GetString(_data, _pos, end - _pos);
            _pos = end + 1;
            return s;
        }
    }
}

using System.Net.Sockets;
using System.Text;

namespace DiscordAdminConsole.Rcon;

public enum RconErrorKind
{
    ConnectFailed,
    AuthFailed,
    Timeout,
    Protocol,
}

public class RconException : Exception
{
    public RconErrorKind Kind { get; }

    public RconException(RconErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

internal sealed class RconConnection : IDisposable
{
    private const int TypeAuth = 3;
    private const int TypeExec = 2;
    private const int TypeResponseValue = 0;
    private const int TypeAuthResponse = 2;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _requestId;

    public string Host { get; }
    public int Port { get; }
    private string Password { get; }

    public RconConnection(string host, int port, string password)
    {
        Host = host;
        Port = port;
        Password = password;
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsAlive())
                await ConnectAndAuthAsync(ct);

            var id = NextId();
            await WritePacketAsync(_stream!, id, TypeExec, command, ct);
            return await ReadResponseAsync(id, ct);
        }
        catch (RconException)
        {
            HardReset();
            throw;
        }
        catch (OperationCanceledException)
        {
            HardReset();
            throw new RconException(RconErrorKind.Timeout, "RCON timeout");
        }
        catch (Exception ex)
        {
            HardReset();
            throw new RconException(RconErrorKind.ConnectFailed, ex.Message, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsAlive() =>
        _client is { Connected: true } && _stream != null;

    private async Task ConnectAndAuthAsync(CancellationToken ct)
    {
        HardReset();
        var client = new TcpClient();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(Host, Port, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new RconException(RconErrorKind.Timeout, $"connect {Host}:{Port} timed out");
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new RconException(RconErrorKind.ConnectFailed, $"connect {Host}:{Port} failed: {ex.Message}", ex);
        }

        _client = client;
        client.NoDelay = true;
        _stream = client.GetStream();

        var authId = NextId();
        await WritePacketAsync(_stream, authId, TypeAuth, Password, ct);

        while (true)
        {
            var (id, type, body) = await ReadPacketAsync(_stream, ct);
            if (type == TypeAuthResponse)
            {
                if (id == -1)
                    throw new RconException(RconErrorKind.AuthFailed, "rcon password rejected");
                if (id != authId)
                    continue;
                break;
            }
        }
    }

    private async Task<string> ReadResponseAsync(int id, CancellationToken ct)
    {
        if (_stream == null)
            throw new RconException(RconErrorKind.Protocol, "no stream");

        var sb = new StringBuilder();
        var total = 0;

        while (true)
        {
            string chunk;
            try
            {
                var (pid, type, body) = await ReadPacketAsync(_stream, ct, quietMs: 400);
                chunk = body;
                if (type != TypeResponseValue || pid != id)
                {
                    if (sb.Length > 0)
                        break;
                    continue;
                }
            }
            catch (RconException ex) when (ex.Kind == RconErrorKind.Timeout)
            {
                break;
            }

            if (chunk.Length == 0)
                break;

            sb.Append(chunk);
            total += chunk.Length;
            if (total >= 64_000)
                break;
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    private async Task WritePacketAsync(NetworkStream stream, int id, int type, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var size = payload.Length + 10;
        var packet = new byte[size + 4];
        WriteInt(packet, 0, size);
        WriteInt(packet, 4, id);
        WriteInt(packet, 8, type);
        Buffer.BlockCopy(payload, 0, packet, 12, payload.Length);
        await stream.WriteAsync(packet, ct);
    }

    private async Task<(int Id, int Type, string Body)> ReadPacketAsync(
        NetworkStream stream, CancellationToken ct, int quietMs = 3500)
    {
        byte[]? header;
        try
        {
            header = await ReadExactAsync(stream, 4, ct, quietMs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RconException(RconErrorKind.Timeout, "read header timeout");
        }
        if (header == null)
            throw new RconException(RconErrorKind.Protocol, "empty header");

        var size = ReadInt(header, 0);
        if (size < 10 || size > 8192 * 16)
            throw new RconException(RconErrorKind.Protocol, $"bad packet size {size}");

        byte[]? buffer;
        try
        {
            buffer = await ReadExactAsync(stream, size, ct, quietMs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RconException(RconErrorKind.Timeout, "read body timeout");
        }
        if (buffer == null)
            throw new RconException(RconErrorKind.Protocol, "empty body");

        var id = ReadInt(buffer, 0);
        var type = ReadInt(buffer, 4);
        var bodyLength = size - 10;
        var body = Encoding.UTF8.GetString(buffer, 8, Math.Max(bodyLength, 0));
        return (id, type, body.TrimEnd('\0'));
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct, int quietMs)
    {
        var buffer = new byte[count];
        var read = 0;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(quietMs));
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), timeoutCts.Token);
            if (n <= 0)
                throw new IOException("connection closed by remote host");
            read += n;
        }
        return buffer;
    }

    private static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt(byte[] buf, int offset) =>
        buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24);

    private int NextId()
    {
        _requestId = (_requestId + 1) & 0x7fffffff;
        if (_requestId == 0)
            _requestId = 1;
        return _requestId;
    }

    private void HardReset()
    {
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
    }

    public void Dispose()
    {
        HardReset();
        _gate.Dispose();
    }
}

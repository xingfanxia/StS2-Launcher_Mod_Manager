using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace STS2MobileIos.Steam;

internal sealed class SteamCmConnection : IDisposable
{
    private const int MaxMessage = 16 * 1024 * 1024;
    private readonly ClientWebSocket _socket = new();
    private readonly Queue<byte[]> _messages = new();
    private ulong _steamId;
    private ulong _session;

    public async Task Probe(HttpClient http, CancellationToken cancellation)
    {
        await Connect(http, cancellation).ConfigureAwait(false);
        await Send(9805, new SteamWire(), new SteamWire().Number(1, 65581), cancellation).ConfigureAwait(false);
        var request = new SteamWire().Number(2, 1).Text(4, "Client").Bytes(3,
            new SteamWire().Text(1, "StS2 iOS protocol test").Number(2, 1).ToArray());
        await Send(9804, new SteamWire().Fixed(10, 1).Text(12, "Authentication.BeginAuthSessionViaQR#1"), request, cancellation).ConfigureAwait(false);
        while (true)
        {
            var message = await Receive(cancellation).ConfigureAwait(false);
            if (message.Type != 147 || Number(message.Header, 11) != 1) continue;
            if (Number(message.Header, 13, 2) != 1 || !Text(message.Body, 2).StartsWith("https://s.team/"))
                throw new InvalidOperationException("Steam protocol login probe failed.");
            return;
        }
    }

    public async Task<JsonNode> Call(HttpClient http, SteamLogin login, string method, JsonObject input, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        await Connect(http, token).ConfigureAwait(false);
        await Send(9805, new SteamWire(), new SteamWire().Number(1, 65581), token).ConfigureAwait(false);
        _steamId = ulong.Parse(login.SteamId);
        await Send(5514, Header(), new SteamWire().Number(1, 65581).Text(6, "english")
            .Number(7, unchecked((uint)-600)).Number(8, 1).Text(50, login.AccountName)
            .Text(96, "StS2 iOS").Text(108, login.RefreshToken), token).ConfigureAwait(false);
        while (true)
        {
            var message = await Receive(token).ConfigureAwait(false);
            if (message.Type != 751) continue;
            ulong result = Number(message.Body, 1, 2);
            if (result != 1)
                throw new InvalidOperationException($"Steam client login failed (result {result}). Reconnect Steam if the login expired.");
            _session = Number(message.Header, 2);
            _steamId = Number(message.Header, 1, _steamId);
            break;
        }

        SteamWire request = method switch {
            "EnumerateUserFiles" => new SteamWire().Number(1, SteamCloudClient.AppId).Number(2, 1)
                .Number(3, 500).Number(4, ulong.Parse(input["start_index"]!.ToString())),
            "ClientFileDownload" => new SteamWire().Number(1, SteamCloudClient.AppId).Text(2, input["filename"]!.ToString()),
            _ => throw new InvalidOperationException("Unsupported cloud operation."),
        };
        const ulong job = 1;
        await Send(151, Header().Fixed(10, job).Text(12, "Cloud." + method + "#1"), request, token).ConfigureAwait(false);
        while (true)
        {
            var message = await Receive(token).ConfigureAwait(false);
            if (message.Type == 757) throw new InvalidOperationException("Steam disconnected. Refresh to reconnect.");
            if (message.Type != 147 || Number(message.Header, 11) != job) continue;
            ulong result = Number(message.Header, 13, 2);
            if (result != 1) throw new InvalidOperationException($"Steam Cloud request failed (result {result}).");
            return DecodeCloud(method, message.Body);
        }
    }

    public async Task Connect(HttpClient http, CancellationToken cancellation)
    {
        string body = await http.GetStringAsync("https://api.steampowered.com/ISteamDirectory/GetCMListForConnect/v1/?cellid=0&cmtype=websockets", cancellation).ConfigureAwait(false);
        var rows = JsonNode.Parse(body)?["response"]?["serverlist"]?.AsArray()
            ?? throw new InvalidOperationException("Steam server discovery failed.");
        // Port 443 works on networks that block Steam's custom client ports.
        string endpoint = rows.Select(row => row?["endpoint"]?.ToString()).FirstOrDefault(value =>
            value != null && value.EndsWith(".steamserver.net:443", StringComparison.OrdinalIgnoreCase)
            && !value.Contains('/') && !value.Contains('@'));
        if (endpoint == null) throw new InvalidOperationException("No Steam server is available on port 443.");
        await _socket.ConnectAsync(new Uri("wss://" + endpoint + "/cmsocket/"), cancellation).ConfigureAwait(false);
    }

    private SteamWire Header() => new SteamWire().Fixed(1, _steamId).Number(2, _session);
    private async Task Send(uint type, SteamWire header, SteamWire body, CancellationToken cancellation)
    {
        byte[] h = header.ToArray(), b = body.ToArray(), packet = new byte[8 + h.Length + b.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, type | 0x80000000);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), h.Length);
        h.CopyTo(packet, 8); b.CopyTo(packet, 8 + h.Length);
        await _socket.SendAsync(packet.AsMemory(), WebSocketMessageType.Binary, true, cancellation).ConfigureAwait(false);
    }

    private sealed record Message(uint Type, List<SteamWire.Field> Header, List<SteamWire.Field> Body);
    private async Task<Message> Receive(CancellationToken cancellation)
    {
        while (true)
        {
            byte[] packet;
            if (_messages.Count > 0) packet = _messages.Dequeue();
            else
            {
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[8192];
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
                    if (result.MessageType != WebSocketMessageType.Binary) throw new InvalidOperationException("Steam connection closed unexpectedly.");
                    if (bytes.Length + result.Count > MaxMessage) throw new InvalidDataException("Steam message is too large.");
                    bytes.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                packet = bytes.ToArray();
            }
            if (packet.Length < 8) throw new InvalidDataException("Truncated Steam packet.");
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(packet);
            if ((type & 0x80000000) == 0) continue;
            type &= 0x7fffffff;
            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4));
            if (headerLength < 0 || headerLength > packet.Length - 8) throw new InvalidDataException("Invalid Steam header.");
            var header = SteamWire.Read(packet.AsSpan(8, headerLength).ToArray());
            var body = SteamWire.Read(packet.AsSpan(8 + headerLength).ToArray());
            if (type != 1) return new(type, header, body);
            byte[] multi = body.FirstOrDefault(field => field.Id == 2).Data ?? Array.Empty<byte>();
            ulong expanded = Number(body, 1);
            if (expanded > MaxMessage) throw new InvalidDataException("Steam message is too large.");
            if (expanded != 0)
            {
                using var gzip = new GZipStream(new MemoryStream(multi), CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[8192];
                int count;
                while ((count = gzip.Read(buffer)) > 0)
                {
                    if (output.Length + count > MaxMessage) throw new InvalidDataException("Steam message is too large.");
                    output.Write(buffer, 0, count);
                }
                multi = output.ToArray();
                if ((ulong)multi.Length != expanded) throw new InvalidDataException("Invalid compressed Steam packet.");
            }
            for (int offset = 0; offset < multi.Length;)
            {
                if (multi.Length - offset < 4) throw new InvalidDataException("Truncated Steam packet list.");
                int size = BinaryPrimitives.ReadInt32LittleEndian(multi.AsSpan(offset));
                offset += 4;
                if (size < 8 || size > multi.Length - offset) throw new InvalidDataException("Invalid Steam packet list.");
                _messages.Enqueue(multi.AsSpan(offset, size).ToArray());
                offset += size;
                if (_messages.Count > 4096) throw new InvalidDataException("Too many Steam messages.");
            }
        }
    }

    private static ulong Number(List<SteamWire.Field> fields, int id, ulong fallback = 0)
        => fields.Any(field => field.Id == id) ? fields.First(field => field.Id == id).Number : fallback;
    private static string Text(List<SteamWire.Field> fields, int id) => fields.FirstOrDefault(field => field.Id == id).Text;
    internal static JsonObject DecodeCloud(string method, List<SteamWire.Field> fields)
    {
        if (method == "EnumerateUserFiles")
        {
            var files = new JsonArray();
            foreach (var field in fields.Where(field => field.Id == 1))
            {
                var file = SteamWire.Read(field.Data);
                files.Add(new JsonObject { ["filename"] = Text(file, 3), ["timestamp"] = Number(file, 4),
                    ["file_size"] = Number(file, 5), ["file_sha"] = Text(file, 10) });
            }
            return new JsonObject { ["files"] = files, ["total_files"] = Number(fields, 2) };
        }
        var headers = new JsonArray();
        foreach (var field in fields.Where(field => field.Id == 10))
        {
            var header = SteamWire.Read(field.Data);
            headers.Add(new JsonObject { ["name"] = Text(header, 1), ["value"] = Text(header, 2) });
        }
        return new JsonObject { ["appid"] = Number(fields, 1), ["file_size"] = Number(fields, 2),
            ["raw_file_size"] = Number(fields, 3), ["sha_file"] = Convert.ToBase64String(fields.FirstOrDefault(field => field.Id == 4).Data ?? Array.Empty<byte>()),
            ["time_stamp"] = Number(fields, 5), ["is_explicit_delete"] = Number(fields, 6) != 0,
            ["url_host"] = Text(fields, 7), ["url_path"] = Text(fields, 8), ["use_https"] = Number(fields, 9) != 0,
            ["request_headers"] = headers, ["encrypted"] = Number(fields, 11) != 0 };
    }
    public void Dispose() => _socket.Dispose();
}

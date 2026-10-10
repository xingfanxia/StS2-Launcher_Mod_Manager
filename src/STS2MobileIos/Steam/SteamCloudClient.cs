using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace STS2MobileIos.Steam;

public sealed record SteamLogin(string AccountName, string RefreshToken, string SteamId);
public sealed record QrChallenge(string ClientId, string RequestId, string Url, double Interval);
public sealed record CloudFile(string Name, long Size, long Timestamp, string Sha1);

// Uses Steam's HTTPS service interface, without runtime protobuf code generation.
public sealed class SteamCloudClient : IDisposable
{
    public const uint AppId = 2868840;
    public const int MaximumSaveBytes = 16 * 1024 * 1024;
    private readonly HttpClient _http;
    private string _accessToken;
    internal Func<string, JsonObject, CancellationToken, Task<JsonNode>> CloudTransport { get; set; }
    public SteamLogin Login { get; private set; }
    public void ForgetLogin() { Login = null; _accessToken = null; }

    public SteamCloudClient(HttpMessageHandler handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<QrChallenge> BeginLogin(CancellationToken cancellation)
    {
        var value = await Call("Authentication", "BeginAuthSessionViaQR", new JsonObject {
            ["device_friendly_name"] = "StS2 iOS", ["platform_type"] = 1, ["website_id"] = "Client",
            ["device_details"] = new JsonObject { ["device_friendly_name"] = "StS2 iOS", ["platform_type"] = 1, ["os_type"] = -600 },
        }, false, cancellation);
        var challenge = new QrChallenge(Text(value, "client_id"), Text(value, "request_id"),
            Text(value, "challenge_url"), Math.Clamp(value["interval"]?.GetValue<double>() ?? 5, 1, 15));
        var uri = new Uri(challenge.Url);
        if (uri.Scheme != "https" || uri.Host != "s.team")
            throw new InvalidOperationException("Steam returned an unexpected login address.");
        return challenge;
    }

    public async Task<SteamLogin> WaitForLogin(QrChallenge challenge, Action<string> changedUrl, CancellationToken cancellation)
    {
        string clientId = challenge.ClientId;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(challenge.Interval), deadline.Token);
            var value = await Call("Authentication", "PollAuthSessionStatus", new JsonObject {
                ["client_id"] = clientId, ["request_id"] = challenge.RequestId,
            }, false, deadline.Token);
            if (!string.IsNullOrEmpty(Text(value, "new_client_id")))
                clientId = Text(value, "new_client_id");
            if (!string.IsNullOrEmpty(Text(value, "new_challenge_url")))
                changedUrl(Text(value, "new_challenge_url"));
            string refresh = Text(value, "refresh_token");
            if (string.IsNullOrEmpty(refresh))
                continue;
            string steamId = TokenSubject(refresh);
            Login = new SteamLogin(Text(value, "account_name"), refresh, steamId);
            _accessToken = Text(value, "access_token");
            return Login;
        }
    }

    public Task Resume(SteamLogin login, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Login = login;
        return Task.CompletedTask;
    }

    public async Task<List<CloudFile>> ListFiles(CancellationToken cancellation)
    {
        var files = new List<CloudFile>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int start = 0; start < 10_000;)
        {
            var value = await Call("Cloud", "EnumerateUserFiles", new JsonObject {
                ["appid"] = AppId, ["extended_details"] = true, ["count"] = 500, ["start_index"] = start,
            }, true, cancellation, get: true);
            var page = value["files"]?.AsArray() ?? new JsonArray();
            foreach (var item in page)
            {
                string name = Text(item, "filename");
                if (!names.Add(name))
                    throw new InvalidOperationException("Steam returned a repeated cloud page; retry browsing.");
                files.Add(new(name, Number(item, "file_size"), Number(item, "timestamp"), Text(item, "file_sha")));
            }
            start += page.Count;
            if (page.Count == 0 || start >= Number(value, "total_files", long.MaxValue))
                return files;
        }
        throw new InvalidOperationException("This account has more cloud files than this preview supports.");
    }

    public async Task<byte[]> Download(CloudFile expected, CancellationToken cancellation)
    {
        if (expected.Size < 0 || expected.Size > MaximumSaveBytes)
            throw new InvalidOperationException("Save exceeds the download size limit.");
        var value = await Call("Cloud", "ClientFileDownload", new JsonObject {
            ["appid"] = AppId, ["filename"] = expected.Name,
        }, true, cancellation, get: true);
        if (Number(value, "appid") != AppId || value["encrypted"]?.GetValue<bool>() == true
            || value["is_explicit_delete"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("Steam returned an unsupported or deleted cloud file.");
        if (Number(value, "time_stamp") != expected.Timestamp)
            throw new InvalidOperationException("Cloud save changed since the preview. Refresh and try again.");
        string sha = Text(value, "sha_file");
        var request = new HttpRequestMessage(HttpMethod.Get, DownloadUri(Text(value, "url_host"), Text(value, "url_path")));
        foreach (var header in value["request_headers"]?.AsArray() ?? new JsonArray())
            request.Headers.TryAddWithoutValidation(Text(header, "name"), Text(header, "value"));
        using (request)
        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Save download failed (HTTP {(int)response.StatusCode}).");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
            {
                if (output.Length + count > MaximumSaveBytes)
                    throw new InvalidOperationException("Save exceeds the download size limit.");
                output.Write(buffer, 0, count);
            }
            byte[] data = CloudSavePolicy.Decode(output.ToArray());
            if (data.Length != expected.Size)
                throw new InvalidOperationException("Cloud save size changed; refresh before downloading.");
            string actual = Convert.ToHexString(SHA1.HashData(data));
            string responseHash = string.IsNullOrEmpty(sha) ? "" : Convert.ToHexString(Convert.FromBase64String(sha));
            if (string.IsNullOrEmpty(responseHash) || !actual.Equals(responseHash, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(expected.Sha1) && !actual.Equals(expected.Sha1, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Cloud save checksum mismatch; no local files were changed.");
            return data;
        }
    }

    internal static Uri DownloadUri(string host, string path)
    {
        // Steam issues signed URLs on regional third-party storage as well as its
        // own domains. Trust the authenticated CM response, with HTTPS and no redirects.
        if (string.IsNullOrEmpty(host) || Uri.CheckHostName(host) != UriHostNameType.Dns
            || !host.Contains('.') || host.EndsWith('.')
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//")
            || path.Contains('\\') || path.Any(char.IsControl)
            || !Uri.TryCreate("https://" + host + path, UriKind.Absolute, out var uri)
            || !uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || uri.Port != 443 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Steam returned an unexpected download host.");
        return uri;
    }

    private async Task<JsonNode> Call(string service, string method, JsonObject input, bool authenticated,
        CancellationToken cancellation, bool get = false)
    {
        if (service == "Cloud")
        {
            if (Login == null) throw new InvalidOperationException("Connect Steam before browsing saves.");
            if (CloudTransport != null) return await CloudTransport(method, input, cancellation);
            using var connection = new SteamCmConnection();
            try { return await connection.Call(_http, Login, method, input, cancellation); }
            catch (System.Net.WebSockets.WebSocketException) { throw new InvalidOperationException("Cannot reach Steam Cloud. Check the connection and retry."); }
        }
        if (authenticated && string.IsNullOrEmpty(_accessToken))
            throw new InvalidOperationException("Connect Steam before browsing saves.");
        string url = $"https://api.steampowered.com/I{service}Service/{method}/v1/";
        var fields = new List<KeyValuePair<string, string>> { new("input_json", input.ToJsonString()) };
        if (authenticated)
            fields.Add(new("access_token", _accessToken));
        using var form = new FormUrlEncodedContent(fields);
        if (get)
            url += "?" + await form.ReadAsStringAsync(cancellation);
        using var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, url);
        if (!get)
            request.Content = form;
        using var response = await _http.SendAsync(request, cancellation);
        // Never include signed URLs, response bodies or credentials in errors/logs.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Steam rejected this session. Disconnect and sign in again.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Steam request failed (HTTP {(int)response.StatusCode}).");
        if (response.Headers.TryGetValues("x-eresult", out var results) && results.First() != "1")
            throw new InvalidOperationException("Steam could not complete this request (result " + results.First() + ").");
        string body = await response.Content.ReadAsStringAsync(cancellation);
        return JsonNode.Parse(body)?["response"] ?? throw new InvalidOperationException("Steam returned an empty response.");
    }

    internal static string TokenSubject(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("Steam returned an invalid login token.");
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        string subject = Text(JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))), "sub");
        if (subject.Length != 17 || !ulong.TryParse(subject, out _))
            throw new InvalidOperationException("Steam returned an invalid account identifier.");
        return subject;
    }

    internal static string Text(JsonNode value, string key) => value?[key]?.ToString() ?? "";
    private static long Number(JsonNode value, string key, long fallback = 0) => long.TryParse(Text(value, key), out long result) ? result : fallback;
    public void Dispose() => _http.Dispose();
}

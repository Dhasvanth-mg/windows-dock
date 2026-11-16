using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal sealed record PlanAccount(string ClientId, string Subject, string Email, string Access, string Refresh,
    DateTimeOffset Expires, string? IdentityToken = null, string? Scopes = null)
{
    public string Label => string.IsNullOrEmpty(Email) ? Subject : Email;
}

internal sealed record PlanModel(string Id, string Name);

internal sealed class PlanException(string message, bool canFallback = false) : Exception(message)
{
    internal bool CanFallback { get; } = canFallback;
}

internal sealed class PlanSignIn
{
    private Task<PlanAccount>? attempt;
    private CancellationTokenSource? cancellation;

    internal Task<PlanAccount>? Active => attempt is { IsCompleted: false } ? attempt : null;

    internal Task<PlanAccount> Start(PlanAccount? account, CancellationToken dockLifetime,
        Func<PlanAccount?, CancellationToken, Task<PlanAccount>> connect) =>
        Active ?? (attempt = RunAsync(account, dockLifetime, connect));

    internal void Cancel() => cancellation?.Cancel();

    private async Task<PlanAccount> RunAsync(PlanAccount? account, CancellationToken dockLifetime,
        Func<PlanAccount?, CancellationToken, Task<PlanAccount>> connect)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(dockLifetime);
        cancellation = request;
        try { return await connect(account, request.Token); }
        finally { cancellation = null; }
    }
}

internal static class ChatGptPlan
{
    private const string Auth = "https://auth.openai.com";
    private const string Resource = "https://api.openai.com/v1";
    private const string Scope = "chatgpt.tokens.use.direct";
    private const string FileName = "chatgpt-accounts.dat";
    private const string CallbackPath = "/auth/callback";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(40) };
    private static readonly SemaphoreSlim TokenGate = new(1);

    internal static PlanAccount[] Load()
    {
        try
        {
            var path = DockData.FilePath(FileName);
            if (!File.Exists(path) || new FileInfo(path).Length > 256_000)
                return [];
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<PlanAccount[]>(clear) ?? []; }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return [];
        }
    }

    private static void Store(PlanAccount account)
    {
        var accounts = Load().Where(item => item.ClientId != account.ClientId).Prepend(account).ToArray();
        var clear = JsonSerializer.SerializeToUtf8Bytes(accounts);
        try { DockData.Write(FileName, path => File.WriteAllBytes(path, ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser))); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal static async Task<PlanAccount> ConnectAsync(PlanAccount? existing, CancellationToken cancellation)
    {
        var host = DockData.Read<string>("chatgpt-host.json");
        if (host == null || !host.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(host[9..], out _))
        {
            host = "urn:uuid:" + Guid.NewGuid();
            DockData.Save("chatgpt-host.json", host);
        }
        var (client, code, redirect, nonce, verifier) = await AuthorizeAsync(existing, host, ShellIntegration.Launch, cancellation);
        using var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = client, ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource
        }, cancellation);
        var root = token.RootElement;
        var identity = await ValidateIdentityAsync(String(root, "id_token") ?? "", client, nonce, cancellation);
        var subject = String(identity, "sub");
        if (string.IsNullOrEmpty(subject) || existing != null && existing.Subject != subject)
            throw new PlanException("The browser returned a different ChatGPT account.");
        var account = ReadTokens(root, new(client, subject, String(identity, "email") ?? "", "", "", default), requireScope: true);
        await TokenGate.WaitAsync(cancellation);
        try { Store(account); }
        finally { TokenGate.Release(); }
        return account;
    }

    internal static async Task<(string Client, string Code, string Redirect, string Nonce, string Verifier)> AuthorizeAsync(
        PlanAccount? existing, string host, Action<string> openBrowser, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{CallbackPath}";
        var state = RandomToken();
        var nonce = RandomToken();
        var verifier = RandomToken();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = existing?.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = host,
            ["response_type"] = "code", ["redirect_uri"] = redirect,
            ["scope"] = $"openid profile email offline_access resource.invoke {Scope}", ["resource"] = Resource,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        };
        if (existing == null)
            query["agent_name_hint"] = "Windows Dock";
        else if (!string.IsNullOrEmpty(existing.IdentityToken))
            query["id_token_hint"] = existing.IdentityToken;
        openBrowser(Auth + "/api/accounts/authorize?" + string.Join("&",
            query.Select(item => item.Key + "=" + Uri.EscapeDataString(item.Value))));
        (string Code, string? Client) callback;
        try { callback = await WaitForCallbackAsync(listener, state, timeout.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new PlanException("Sign-in timed out. Start a new sign-in from Windows Dock.");
        }
        var client = existing?.ClientId ?? callback.Client;
        if (client == null || !client.StartsWith("oaiapp_", StringComparison.Ordinal) || client.Length > 256 ||
            callback.Client != null && callback.Client != client)
            throw new PlanException("ChatGPT did not return a valid app registration.");
        return (client, callback.Code, redirect, nonce, verifier);
    }

    internal static async Task<bool> DisconnectAsync(PlanAccount account, CancellationToken cancellation)
    {
        await TokenGate.WaitAsync(cancellation);
        try
        {
            account = Load().FirstOrDefault(item => item.ClientId == account.ClientId) ?? account;
            var revoked = string.IsNullOrEmpty(account.Refresh);
            if (!revoked)
            {
                try
                {
                    using var response = await Http.PostAsync(Auth + "/api/accounts/oauth/revoke",
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["client_id"] = account.ClientId, ["token"] = account.Refresh, ["token_type_hint"] = "refresh_token"
                        }), cancellation);
                    revoked = response.IsSuccessStatusCode;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            }
            Store(account with { Access = "", Refresh = "", IdentityToken = null, Scopes = null, Expires = default });
            return revoked;
        }
        finally { TokenGate.Release(); }
    }

    internal static async Task<PlanModel[]> ModelsAsync(PlanAccount account, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Resource + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(account, cancellation));
        using var response = await Http.SendAsync(request, cancellation);
        await EnsureResponseAsync(response, cancellation);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellation);
        var models = json == null ? default : Property(json.RootElement, "models");
        return models.ValueKind == JsonValueKind.Array
            ? models.EnumerateArray().Where(model => String(model, "visibility") == "list")
                .Select(model => (Id: String(model, "slug"), Name: String(model, "display_name")))
                .Where(model => !string.IsNullOrEmpty(model.Id)).Select(model => new PlanModel(model.Id!, model.Name ?? model.Id!)).ToArray()
            : [];
    }

    internal static async Task<string> SendAsync(PlanAccount account, string model, string instructions,
        (bool User, string Text)[] turns, CancellationToken cancellation, IReadOnlyDictionary<int, ChatMedia[]>? media = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, Resource + "/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(account, token));
        request.Content = JsonContent.Create(new
        {
            model, instructions, store = false, stream = true,
            input = turns.Select((turn, index) => new { role = turn.User ? "user" : "assistant",
                content = ChatMediaRequest.Content(AssistantProvider.ChatGptPlan, turn.Text, media?.GetValueOrDefault(index)) })
        });
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await EnsureResponseAsync(response, token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        return await ReadStreamAsync(reader, token);
    }

    private static async Task<string> ReadStreamAsync(TextReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal) || line == "data: [DONE]")
                continue;
            using var data = JsonDocument.Parse(line.AsMemory(6));
            var root = data.RootElement;
            switch (String(root, "type"))
            {
                case "response.output_text.delta":
                    if (text.Length < 40_000)
                        text.Append(String(root, "delta"));
                    break;
                case "response.completed": return text.ToString();
                case "response.incomplete": throw new PlanException("ChatGPT returned an incomplete response.");
                case "response.failed": throw ResponseError(String(Property(Property(root, "response"), "error"), "code"));
                case "error": throw ResponseError(String(root, "code") ?? String(Property(root, "error"), "code"));
            }
        }
        throw new PlanException("The ChatGPT response ended before completion.");
    }

    private static async Task<string> AccessAsync(PlanAccount account, CancellationToken token)
    {
        await TokenGate.WaitAsync(token);
        try
        {
            account = Load().FirstOrDefault(item => item.ClientId == account.ClientId && item.Subject == account.Subject)
                ?? throw new PlanException("Connect your ChatGPT account first.");
            if (account.Expires > DateTimeOffset.UtcNow.AddMinutes(2) && account.Access.Length > 0)
                return account.Access;
            if (account.Refresh.Length == 0)
                throw new PlanException("Reconnect your ChatGPT account.");
            try
            {
                using var response = await ExchangeAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = account.ClientId,
                    ["refresh_token"] = account.Refresh, ["resource"] = Resource
                }, token);
                var updated = ReadTokens(response.RootElement, account, requireScope: false);
                Store(updated);
                return updated.Access;
            }
            catch (PlanException error) when (error.Message == "Reconnect your ChatGPT account.")
            {
                Store(account with { Access = "", Refresh = "", IdentityToken = null, Scopes = null, Expires = default });
                throw;
            }
        }
        finally { TokenGate.Release(); }
    }

    private static PlanAccount ReadTokens(JsonElement root, PlanAccount account, bool requireScope)
    {
        var scope = String(root, "scope");
        if ((requireScope || scope != null) && scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(Scope) != true)
            throw new PlanException("ChatGPT plan usage was not granted for this account.");
        var access = String(root, "access_token");
        var refresh = String(root, "refresh_token") ?? account.Refresh;
        var expiry = Count(root, "expires_in");
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh) || expiry is not (> 0 and <= 2592000))
            throw new PlanException("The ChatGPT account returned incomplete credentials.");
        return account with
        {
            Access = access, Refresh = refresh, Expires = DateTimeOffset.UtcNow.AddSeconds(expiry.Value),
            IdentityToken = requireScope ? String(root, "id_token") : account.IdentityToken, Scopes = scope ?? account.Scopes
        };
    }

    private static async Task<JsonDocument> ExchangeAsync(Dictionary<string, string> data, CancellationToken token)
    {
        using var response = await Http.PostAsync(Auth + "/api/accounts/oauth/token", new FormUrlEncodedContent(data), token);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token)
                ?? throw new PlanException("ChatGPT returned an empty token response.");
        var code = await ErrorCodeAsync(response, token);
        if (code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
            throw new PlanException("Reconnect your ChatGPT account.");
        throw ResponseError(code, response.StatusCode);
    }

    private static async Task EnsureResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
            throw ResponseError(await ErrorCodeAsync(response, token), response.StatusCode);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            return error == null ? null : String(Property(error.RootElement, "error"), "code") ?? String(error.RootElement, "error");
        }
        catch (JsonException) { return null; }
    }

    private static PlanException ResponseError(string? code, HttpStatusCode? status = null) => code switch
    {
        "subscription_sharing_usage_limit_exceeded" => new("ChatGPT reached the plan or app usage limit. Check Manage usage.", true),
        "subscription_sharing_usage_unavailable" => new("ChatGPT could not check plan usage. Try again later.", true),
        "subscription_sharing_user_not_eligible" => new("ChatGPT plan usage is unavailable for this account or workspace.", true),
        _ when status == HttpStatusCode.TooManyRequests => new("ChatGPT is rate limited. Check Manage usage.", true),
        _ when status == HttpStatusCode.Unauthorized => new("Reconnect your ChatGPT account."),
        _ when status == HttpStatusCode.ServiceUnavailable => new("ChatGPT plan access is temporarily unavailable.", true),
        _ => new("ChatGPT could not complete the request. Check the account and model.")
    };

    private static async Task<JsonElement> ValidateIdentityAsync(string jwt, string client, string nonce, CancellationToken token)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
            throw new PlanException("ChatGPT returned an invalid identity token.");
        using var header = JsonDocument.Parse(Decode(parts[0]));
        var kid = String(header.RootElement, "kid");
        if (string.IsNullOrEmpty(kid) || String(header.RootElement, "alg") != "RS256")
            throw new PlanException("ChatGPT returned an unsupported identity signature.");
        using var keys = await Http.GetFromJsonAsync<JsonDocument>(Auth + "/.well-known/jwks.json", token);
        var jwks = keys == null ? default : Property(keys.RootElement, "keys");
        var key = jwks.ValueKind == JsonValueKind.Array ? jwks.EnumerateArray().FirstOrDefault(item =>
            String(item, "kid") == kid && String(item, "kty") == "RSA" && String(item, "use") == "sig") : default;
        if (String(key, "n") is not { } modulus || String(key, "e") is not { } exponent)
            throw new PlanException("ChatGPT’s identity signing key was unavailable.");
        using var rsa = RSA.Create(new RSAParameters { Modulus = Decode(modulus), Exponent = Decode(exponent) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new PlanException("ChatGPT’s identity signature could not be verified.");
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        var root = claims.RootElement;
        var audiences = Property(root, "aud");
        var validAudience = String(root, "aud") == client ||
            audiences.ValueKind == JsonValueKind.Array && audiences.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == client)
            && (audiences.GetArrayLength() == 1 || String(root, "azp") == client);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!validAudience || String(root, "iss") != Auth || String(root, "nonce") != nonce ||
            (Count(root, "exp") ?? 0) <= now || (Count(root, "nbf") ?? 0) > now + 60)
            throw new PlanException("ChatGPT’s account identity could not be verified.");
        return root.Clone();
    }

    private static async Task<(string Code, string? Client)> WaitForCallbackAsync(TcpListener listener, string state, CancellationToken cancellation)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var stream = client.GetStream();
            try
            {
                var data = new byte[8192];
                var read = 0;
                while (read < data.Length)
                {
                    var size = await stream.ReadAsync(data.AsMemory(read), timeout.Token);
                    if (size == 0)
                        break;
                    read += size;
                    if (data.AsSpan(0, read).IndexOf("\r\n\r\n"u8) >= 0)
                        break;
                }
                var end = data.AsSpan(0, read).IndexOf("\r\n"u8);
                var target = end < 0 ? [] : Encoding.ASCII.GetString(data, 0, end).Split(' ', 3);
                if (target is not ["GET", var path, _] || !path.StartsWith(CallbackPath + "?", StringComparison.Ordinal))
                {
                    await ReplyAsync(stream, false, timeout.Token);
                    continue;
                }
                var query = System.Web.HttpUtility.ParseQueryString(path[(CallbackPath.Length + 1)..]);
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(query["state"] ?? ""), Encoding.UTF8.GetBytes(state)))
                {
                    await ReplyAsync(stream, false, timeout.Token);
                    continue;
                }
                if (query["error"] != null || query["code"] is not { Length: > 0 } code)
                {
                    await ReplyAsync(stream, false, timeout.Token);
                    throw new PlanException("ChatGPT sign-in was declined or interrupted.");
                }
                try { await ReplyAsync(stream, true, timeout.Token); }
                catch (IOException) { }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
                return (code, query["client_id"]);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            catch (IOException) { }
        }

        static Task ReplyAsync(Stream stream, bool accepted, CancellationToken token)
        {
            var body = accepted ? "Return to Windows Dock to finish connecting your account." : "This sign-in callback was not accepted.";
            var code = accepted ? "200 OK" : "400 Bad Request";
            return stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code}\r\nContent-Type: text/plain\r\n"
                + $"Referrer-Policy: no-referrer\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}"), token).AsTask();
        }
    }

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
}

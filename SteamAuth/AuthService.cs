using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SyntaX
{
    internal sealed class KeyEntry
    {
        public string Key { get; set; }
        public int AccountLimit { get; set; }
    }

    internal sealed class AuthSession
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public DateTime SavedUtc { get; set; }
        public string LicenseKey { get; set; }
    }

    internal sealed class AuthApiResult
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public string Message { get; set; }
        public string Username { get; set; }
        public int? RetryAfterSeconds { get; set; }
    }

    internal sealed class AccountsLoadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int AllowedCount { get; set; }
        public List<SteamAccount> Accounts { get; set; } = new List<SteamAccount>();
    }

    internal static class AuthService
    {
        private static HttpClient _http;
        private static string _memoryLicenseKey;
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private static HttpClient Http => _http ?? (_http = CreateHttpClient());

        private static string SessionPath => RuntimeSecrets.SessionFile;
        private static string SidecarPath => RuntimeSecrets.SidecarFile;

        internal static bool EnsureLocalFolder()
        {
            try
            {
                string path = RuntimeSecrets.LocalAppFolder;
                Directory.CreateDirectory(path);
                if (!Directory.Exists(path))
                    return false;

                try
                {
                    var dir = new DirectoryInfo(path);
                    if ((dir.Attributes & FileAttributes.System) != 0)
                        dir.Attributes &= ~FileAttributes.System;
                    if ((dir.Attributes & FileAttributes.Hidden) == 0)
                        dir.Attributes |= FileAttributes.Hidden;
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            NetworkGuard.RunChecks();
            var handler = NetworkGuard.CreateSecureHandler();
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public static string GetCurrentLicenseKey()
        {
            if (!string.IsNullOrEmpty(_memoryLicenseKey))
                return _memoryLicenseKey;

            var session = LoadSession();
            if (session == null)
                return null;

            if (!string.IsNullOrEmpty(session.LicenseKey))
            {
                _memoryLicenseKey = NormalizeKey(session.LicenseKey);
                return _memoryLicenseKey;
            }

            if (string.IsNullOrEmpty(session.Token))
                return null;

            string sidecarKey = ReadStoredLicenseKey(session.Token);
            if (!string.IsNullOrEmpty(sidecarKey))
                _memoryLicenseKey = NormalizeKey(sidecarKey);
            return _memoryLicenseKey;
        }

        public static AuthSession LoadSession()
        {
            try
            {
                string raw = SecureLocalStore.ReadTextAndUpgrade(SessionPath);
                if (string.IsNullOrEmpty(raw))
                    return null;

                var session = JsonSerializer.Deserialize<AuthSession>(raw, JsonOpts);
                if (session != null && !string.IsNullOrEmpty(session.LicenseKey))
                    _memoryLicenseKey = NormalizeKey(session.LicenseKey);
                return session;
            }
            catch { return null; }
        }

        public static bool SaveSession(string token, string username, string licenseKey = null)
        {
            EnsureLocalFolder();
            string normalizedKey = NormalizeKey(licenseKey);
            if (!string.IsNullOrEmpty(normalizedKey))
                _memoryLicenseKey = normalizedKey;

            var session = new AuthSession
            {
                Token = token,
                Username = username,
                SavedUtc = DateTime.UtcNow,
                LicenseKey = normalizedKey
            };

            string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = false });
            if (!SecureLocalStore.WriteText(SessionPath, json))
                return false;

            if (!string.IsNullOrEmpty(normalizedKey))
                SaveLicenseKeySidecar(token, normalizedKey);
            return true;
        }

        public static void ClearSession()
        {
            _memoryLicenseKey = null;
            SecureLocalStore.Delete(SessionPath);
            SecureLocalStore.Delete(SidecarPath);
            AccountSwitchService.Clear();
        }

        public static async Task<AuthApiResult> LoginWithKeyAsync(string licenseKey)
        {
            var spam = LoginCooldown.CheckBeforeAttempt();
            if (!spam.Allowed)
            {
                return new AuthApiResult
                {
                    Success = false,
                    Message = spam.Message,
                    RetryAfterSeconds = spam.RetryAfterSeconds
                };
            }

            LoginCooldown.RecordAttempt();
            licenseKey = NormalizeKey(licenseKey);
            if (string.IsNullOrEmpty(licenseKey))
            {
                LoginCooldown.RecordFailure(3);
                return new AuthApiResult { Success = false, Message = "License key is required." };
            }

            try
            {
                var entries = await FetchKeyEntriesAsync();
                if (entries.Count == 0)
                {
                    LoginCooldown.RecordFailure(5);
                    return new AuthApiResult { Success = false, Message = "Could not reach license service. Try again later." };
                }

                var entry = entries.FirstOrDefault(e => string.Equals(e.Key, licenseKey, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    LoginCooldown.RecordFailure(null);
                    return new AuthApiResult { Success = false, Message = "Invalid license key." };
                }

                LoginCooldown.Reset();
                string token = CreateSessionToken(licenseKey);
                return new AuthApiResult
                {
                    Success = true,
                    Token = token,
                    Username = MaskKey(licenseKey),
                    Message = "License key accepted."
                };
            }
            catch
            {
                LoginCooldown.RecordFailure(5);
                return new AuthApiResult { Success = false, Message = "Authentication failed. Please try again later." };
            }
        }

        public static async Task<AccountsLoadResult> FetchAllUniqueValidAccountsAsync()
        {
            try
            {
                var lines = new List<string>();
                foreach (string url in RuntimeSecrets.RemoteAccountsUrls)
                {
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    string accountsRaw = await FetchRawTextAsync(url, null, RuntimeSecrets.RemoteAccessKey);
                    if (IsBlockedResponse(accountsRaw))
                        continue;

                    lines.AddRange(ExtractAccountLines(accountsRaw));
                }

                if (lines.Count == 0)
                    return FailAccounts("Could not load account data.");

                Program.ParseAccountsFromText(string.Join(Environment.NewLine, lines));
                var accounts = Program.accounts.Where(a => a != null && a.IsValid).ToList();

                if (accounts.Count == 0)
                    return FailAccounts("No valid accounts available.");

                Program.accounts.Clear();
                Program.accounts.AddRange(accounts);

                return new AccountsLoadResult
                {
                    Success = true,
                    Message = "Loaded " + accounts.Count + " accounts.",
                    AllowedCount = accounts.Count,
                    Accounts = accounts
                };
            }
            catch
            {
                return FailAccounts("Failed to load accounts. Please try again later.");
            }
        }

        public static async Task<bool> HealthCheckAsync()
        {
            try
            {
                var req = BuildRequest(HttpMethod.Get, RuntimeSecrets.RemoteDataUrl);
                var resp = await Http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public static async Task<AuthApiResult> ValidateSessionAsync(string token)
        {
            try
            {
                var session = LoadSession();
                if (session == null || string.IsNullOrEmpty(token))
                    return new AuthApiResult { Success = false, Message = "No session." };

                if (!string.Equals(session.Token, token, StringComparison.Ordinal))
                    return new AuthApiResult { Success = false, Message = "Session mismatch." };

                if ((DateTime.UtcNow - session.SavedUtc).TotalDays > RuntimeSecrets.SessionDays)
                    return new AuthApiResult { Success = false, Message = "Session expired." };

                string licenseKey = ReadStoredLicenseKey(token);
                if (string.IsNullOrEmpty(licenseKey) && !string.IsNullOrEmpty(session.LicenseKey))
                    licenseKey = NormalizeKey(session.LicenseKey);
                if (string.IsNullOrEmpty(licenseKey))
                    return new AuthApiResult { Success = false, Message = "Invalid session token." };

                _memoryLicenseKey = NormalizeKey(licenseKey);

                try
                {
                    var entries = await FetchKeyEntriesAsync();
                    if (entries.Count > 0
                        && !entries.Any(e => string.Equals(e.Key, licenseKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new AuthApiResult { Success = false, Message = "License key no longer valid." };
                    }
                }
                catch
                {
                    // Offline or blocked network: keep the local session.
                }

                return new AuthApiResult
                {
                    Success = true,
                    Token = token,
                    Username = session.Username ?? MaskKey(licenseKey),
                    Message = "Session valid."
                };
            }
            catch
            {
                return new AuthApiResult { Success = false, Message = "Could not validate session." };
            }
        }

        private static AccountsLoadResult FailAccounts(string message)
            => new AccountsLoadResult { Success = false, Message = message };

        private static async Task<List<KeyEntry>> FetchKeyEntriesAsync()
        {
            var entries = new List<KeyEntry>();
            string text = await FetchRawTextAsync(RuntimeSecrets.RemoteDataUrl, null);
            if (!IsBlockedResponse(text))
                ParseKeyEntriesInto(text, entries);

            return entries;
        }

        private static void ParseKeyEntriesInto(string text, List<KeyEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (text.TrimStart().StartsWith("[") || text.TrimStart().StartsWith("{"))
            {
                try
                {
                    using (var doc = JsonDocument.Parse(text))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in doc.RootElement.EnumerateArray())
                                AddKeyEntryFromJson(item, entries);
                            return;
                        }
                        if (doc.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in keys.EnumerateArray())
                                AddKeyEntryFromJson(item, entries);
                            return;
                        }
                    }
                }
                catch { }
            }

            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseKeyEntryLine(line, out string key, out int count))
                {
                    entries.Add(new KeyEntry { Key = key, AccountLimit = count });
                }
            }
        }

        private static void AddKeyEntryFromJson(JsonElement item, List<KeyEntry> entries)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                if (TryParseKeyEntryLine(item.GetString(), out string key, out int count))
                    entries.Add(new KeyEntry { Key = key, AccountLimit = count });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                string key = null;
                int count = 1;
                if (item.TryGetProperty("key", out var k)) key = NormalizeKey(k.GetString());
                if (item.TryGetProperty("count", out var c) && c.TryGetInt32(out int n)) count = Math.Max(1, n);
                if (!string.IsNullOrEmpty(key))
                    entries.Add(new KeyEntry { Key = key, AccountLimit = count });
            }
        }

        private static bool TryParseKeyEntryLine(string line, out string key, out int count)
        {
            key = null;
            count = 1;
            line = (line ?? "").Trim();
            if (string.IsNullOrEmpty(line)) return false;

            if (line.Contains("."))
                return false;

            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                key = NormalizeKey(line.Substring(0, colon));
                if (!int.TryParse(line.Substring(colon + 1).Trim(), out count) || count < 1)
                    count = 1;
            }
            else
            {
                key = NormalizeKey(line);
            }

            return !string.IsNullOrEmpty(key);
        }

        private static bool TryExtractSectionForKey(string text, string licenseKey, int limit, out List<string> lines)
        {
            lines = new List<string>();
            string currentKey = null;
            int currentLimit = 0;
            var bucket = new List<string>();

            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (TryParseKeyEntryLine(line, out string headerKey, out int headerCount))
                {
                    if (currentKey != null
                        && string.Equals(currentKey, licenseKey, StringComparison.OrdinalIgnoreCase)
                        && bucket.Count > 0)
                    {
                        lines = bucket.Take(currentLimit > 0 ? currentLimit : limit).ToList();
                        return true;
                    }

                    currentKey = headerKey;
                    currentLimit = headerCount;
                    bucket.Clear();
                    continue;
                }

                if (line.Contains(".") && currentKey != null)
                    bucket.Add(line);
            }

            if (currentKey != null
                && string.Equals(currentKey, licenseKey, StringComparison.OrdinalIgnoreCase)
                && bucket.Count > 0)
            {
                lines = bucket.Take(currentLimit > 0 ? currentLimit : limit).ToList();
                return true;
            }

            lines = new List<string>();
            return false;
        }

        private static List<string> ExtractAccountLines(string text)
        {
            var result = new List<string>();
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Contains(".") && !TryParseKeyEntryLine(line, out _, out _))
                    result.Add(line);
            }
            return result;
        }

        private static List<SteamAccount> SelectLatestValidAccounts(string text, int limit, int skipFromEnd)
        {
            if (limit <= 0 || string.IsNullOrWhiteSpace(text))
                return new List<SteamAccount>();

            var lines = ExtractAccountLines(text);
            if (lines.Count == 0)
                return new List<SteamAccount>();

            Program.ParseAccountsFromText(string.Join(Environment.NewLine, lines));
            var all = Program.accounts.Where(a => a != null).ToList();
            if (all.Count == 0)
                return new List<SteamAccount>();

            int endExclusive = all.Count - Math.Max(0, skipFromEnd);
            if (endExclusive <= 0)
                return new List<SteamAccount>();

            int start = Math.Max(0, endExclusive - limit);
            int count = endExclusive - start;
            var selected = all.GetRange(start, count);

            Program.accounts.Clear();
            Program.accounts.AddRange(selected);

            return selected;
        }

        private static async Task<string> FetchRawTextAsync(string url, string licenseKey, string apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            // Check if it's a local file
            if (File.Exists(url))
            {
                return await Task.FromResult(File.ReadAllText(url));
            }

            string requestUrl = url;
            if (!string.IsNullOrEmpty(licenseKey) && !url.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                string sep = url.Contains("?") ? "&" : "?";
                requestUrl = url + sep + "key=" + Uri.EscapeDataString(licenseKey);
            }

            var req = BuildRequest(HttpMethod.Get, requestUrl, apiKey);
            if (!string.IsNullOrEmpty(licenseKey) && !url.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                req.Headers.TryAddWithoutValidation("X-License-Key", licenseKey);

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;
            return await resp.Content.ReadAsStringAsync();
        }

        private static bool IsHtmlErrorPage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("404 Not Found", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Server Error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBlockedResponse(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                || IsHtmlErrorPage(text)
                || text.IndexOf("Bahright", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string apiKey = null)
        {
            var req = new HttpRequestMessage(method, url);
            string key = apiKey ?? RuntimeSecrets.RemoteAccessKey;
            if (!string.IsNullOrEmpty(key))
                req.Headers.TryAddWithoutValidation(RuntimeSecrets.HeaderName, key);
            req.Headers.TryAddWithoutValidation("User-Agent", RuntimeSecrets.UserAgent);
            return req;
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            int colon = value.IndexOf(':');
            if (colon > 0)
                value = value.Substring(0, colon);
            return value.Trim().ToUpperInvariant();
        }

        private static string MaskKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length <= 4) return key;
            return key.Substring(0, 4) + "****";
        }

        private static string CreateSessionToken(string licenseKey)
        {
            var payload = NormalizeKey(licenseKey) + "|" + DateTime.UtcNow.Ticks;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return Convert.ToBase64String(hash);
            }
        }

        private static void SaveLicenseKeySidecar(string token, string licenseKey)
        {
            try
            {
                string normalized = NormalizeKey(licenseKey);
                SecureLocalStore.WriteText(SidecarPath, token + "|" + normalized);
            }
            catch { }
        }

        private static string ReadStoredLicenseKey(string token)
        {
            try
            {
                string line = SecureLocalStore.ReadTextAndUpgrade(SidecarPath);
                if (string.IsNullOrEmpty(line))
                    return null;

                var parts = line.Split(new[] { '|' }, 2);
                if (parts.Length != 2 || !string.Equals(parts[0], token, StringComparison.Ordinal))
                    return null;

                string keyPart = parts[1];
                try
                {
                    return NormalizeKey(Encoding.UTF8.GetString(Convert.FromBase64String(keyPart)));
                }
                catch
                {
                    return NormalizeKey(keyPart);
                }
            }
            catch { return null; }
        }
    }
}

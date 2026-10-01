using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SyntaX
{
    internal sealed class BookmarkedAccount
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string SteamId { get; set; }
        public string Username { get; set; }
        public string FullKey { get; set; }
        public long SavedUtcUnix { get; set; }
    }

    internal sealed class UserAppConfig
    {
        public string ThemeId { get; set; } = AppTheme.Classic.Id;
        public Dictionary<string, List<BookmarkedAccount>> BookmarksByLicense { get; set; }
            = new Dictionary<string, List<BookmarkedAccount>>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class UserConfigStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private static string ConfigPath => Path.Combine(RuntimeSecrets.LocalAppFolder, ".st4");
        private static UserAppConfig _cache;

        public static UserAppConfig Load()
        {
            if (_cache != null)
                return _cache;

            try
            {
                AuthService.EnsureLocalFolder();
                string raw = SecureLocalStore.ReadTextAndUpgrade(ConfigPath);
                if (string.IsNullOrEmpty(raw))
                {
                    _cache = new UserAppConfig();
                    return _cache;
                }

                _cache = JsonSerializer.Deserialize<UserAppConfig>(raw, JsonOpts) ?? new UserAppConfig();
            }
            catch
            {
                _cache = new UserAppConfig();
            }

            if (string.IsNullOrWhiteSpace(_cache.ThemeId))
                _cache.ThemeId = AppTheme.Classic.Id;
            if (_cache.BookmarksByLicense == null)
                _cache.BookmarksByLicense = new Dictionary<string, List<BookmarkedAccount>>(StringComparer.OrdinalIgnoreCase);

            return _cache;
        }

        public static bool Save()
        {
            if (_cache == null)
                return false;

            try
            {
                string json = JsonSerializer.Serialize(_cache, JsonOpts);
                return SecureLocalStore.WriteText(ConfigPath, json);
            }
            catch
            {
                return false;
            }
        }

        public static string GetThemeId() => Load().ThemeId ?? AppTheme.Classic.Id;

        public static bool SetThemeId(string themeId)
        {
            Load().ThemeId = themeId ?? AppTheme.Classic.Id;
            return Save();
        }

        private static string NormalizeLicense(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) return "";
            licenseKey = licenseKey.Trim();
            int colon = licenseKey.IndexOf(':');
            if (colon > 0)
                licenseKey = licenseKey.Substring(0, colon);
            return licenseKey.Trim().ToUpperInvariant();
        }

        private static List<BookmarkedAccount> GetBookmarkList(string licenseKey)
        {
            licenseKey = NormalizeLicense(licenseKey);
            var config = Load();
            if (!config.BookmarksByLicense.TryGetValue(licenseKey, out var list) || list == null)
            {
                list = new List<BookmarkedAccount>();
                config.BookmarksByLicense[licenseKey] = list;
            }
            return list;
        }

        public static IReadOnlyList<BookmarkedAccount> GetBookmarks(string licenseKey)
            => GetBookmarkList(licenseKey).OrderByDescending(b => b.SavedUtcUnix).ToList();

        public static BookmarkedAccount AddBookmark(string licenseKey, string label, SteamAccount account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.FullKey))
                return null;

            licenseKey = NormalizeLicense(licenseKey);
            if (string.IsNullOrEmpty(licenseKey))
                return null;

            label = (label ?? "").Trim();
            if (string.IsNullOrEmpty(label))
                label = account.Username ?? "Saved account";

            var list = GetBookmarkList(licenseKey);
            var existing = list.FirstOrDefault(b =>
                !string.IsNullOrEmpty(account.FullKey)
                && string.Equals(b.FullKey, account.FullKey, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Label = label;
                existing.Username = account.Username;
                existing.SteamId = account.SteamId;
                existing.FullKey = account.FullKey;
                existing.SavedUtcUnix = NowUnix();
                return Save() ? existing : null;
            }

            var bookmark = new BookmarkedAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label,
                SteamId = account.SteamId,
                Username = account.Username,
                FullKey = account.FullKey,
                SavedUtcUnix = NowUnix()
            };
            list.Add(bookmark);
            return Save() ? bookmark : null;
        }

        public static bool RemoveBookmark(string licenseKey, string bookmarkId)
        {
            var list = GetBookmarkList(licenseKey);
            int removed = list.RemoveAll(b => string.Equals(b.Id, bookmarkId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                return Save();
            return removed > 0;
        }

        public static SteamAccount ResolveBookmark(BookmarkedAccount bookmark, List<SteamAccount> pool)
        {
            if (bookmark == null)
                return null;

            if (pool != null)
            {
                if (!string.IsNullOrEmpty(bookmark.FullKey))
                {
                    var byKey = pool.FirstOrDefault(a => a != null && a.IsValid
                        && string.Equals(a.FullKey, bookmark.FullKey, StringComparison.Ordinal));
                    if (byKey != null)
                        return byKey;
                }
                if (!string.IsNullOrEmpty(bookmark.SteamId))
                {
                    var bySteam = pool.FirstOrDefault(a => a != null && a.IsValid
                        && string.Equals(a.SteamId, bookmark.SteamId, StringComparison.OrdinalIgnoreCase));
                    if (bySteam != null)
                        return bySteam;
                }
            }

            if (string.IsNullOrWhiteSpace(bookmark.FullKey))
                return null;

            Program.ParseAccountsFromText(bookmark.FullKey);
            var parsed = Program.accounts.FirstOrDefault(a => a != null && a.IsValid);
            if (parsed == null)
                return null;

            parsed.FullKey = bookmark.FullKey;
            if (!string.IsNullOrEmpty(bookmark.SteamId))
                parsed.SteamId = bookmark.SteamId;
            if (!string.IsNullOrEmpty(bookmark.Username))
                parsed.Username = bookmark.Username;
            return parsed;
        }

        public static void Clear()
        {
            _cache = null;
            SecureLocalStore.Delete(ConfigPath);
        }

        private static long NowUnix()
            => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}

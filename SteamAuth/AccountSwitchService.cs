using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SyntaX
{
    internal sealed class SavedAccountState
    {
        public string LicenseKey { get; set; }
        public string SteamId { get; set; }
        public string Username { get; set; }
        public string FullKey { get; set; }
        public long CooldownUntilUnix { get; set; }
    }

    internal static class AccountSwitchService
    {
        private const int CooldownSeconds = 5 * 60;
        private static long _activeCooldownUntilUnix;
        private static SavedAccountState _cachedState;

        private static string StatePath => Path.Combine(RuntimeSecrets.LocalAppFolder, ".st3");

        public static bool IsCooldownActive => RemainingSeconds > 0;

        public static int RemainingSeconds
        {
            get
            {
                long until = Math.Max(_activeCooldownUntilUnix, _cachedState?.CooldownUntilUnix ?? 0);
                if (until <= 0)
                    until = LoadCooldownUntilFromDisk();

                if (until <= 0)
                    return 0;

                long remaining = until - NowUnix();
                if (remaining <= 0)
                {
                    _activeCooldownUntilUnix = 0;
                    if (_cachedState != null)
                        _cachedState.CooldownUntilUnix = 0;
                    return 0;
                }

                _activeCooldownUntilUnix = until;
                return Math.Max(1, (int)remaining);
            }
        }

        public static void LoadForLicenseKey(string licenseKey)
        {
            licenseKey = NormalizeKey(licenseKey);
            _cachedState = ReadStateFromDisk();
            if (_cachedState == null || !string.Equals(_cachedState.LicenseKey, licenseKey, StringComparison.OrdinalIgnoreCase))
            {
                _cachedState = new SavedAccountState { LicenseKey = licenseKey };
                _activeCooldownUntilUnix = 0;
                return;
            }

            _activeCooldownUntilUnix = _cachedState.CooldownUntilUnix > NowUnix()
                ? _cachedState.CooldownUntilUnix
                : 0;
            if (_activeCooldownUntilUnix == 0)
                _cachedState.CooldownUntilUnix = 0;
        }

        public static SteamAccount ResolveSavedAccount(string licenseKey, System.Collections.Generic.List<SteamAccount> pool)
        {
            licenseKey = NormalizeKey(licenseKey);
            LoadForLicenseKey(licenseKey);

            if (_cachedState == null || string.IsNullOrEmpty(_cachedState.SteamId))
                return null;

            if (pool != null)
            {
                var bySteam = pool.FirstOrDefault(a => a != null && a.IsValid
                    && string.Equals(a.SteamId, _cachedState.SteamId, StringComparison.OrdinalIgnoreCase));
                if (bySteam != null)
                    return bySteam;

                if (!string.IsNullOrEmpty(_cachedState.Username))
                {
                    var byUser = pool.FirstOrDefault(a => a != null && a.IsValid
                        && string.Equals(a.Username, _cachedState.Username, StringComparison.OrdinalIgnoreCase));
                    if (byUser != null)
                        return byUser;
                }

                if (!string.IsNullOrEmpty(_cachedState.FullKey))
                {
                    var byKey = pool.FirstOrDefault(a => a != null && a.IsValid
                        && string.Equals(a.FullKey, _cachedState.FullKey, StringComparison.Ordinal));
                    if (byKey != null)
                        return byKey;
                }
            }

            return BuildAccountFromSavedState(_cachedState);
        }

        public static bool SaveActiveAccount(string licenseKey, SteamAccount account, bool startCooldown)
        {
            licenseKey = NormalizeKey(licenseKey);
            if (account == null)
                return false;

            long cooldown = startCooldown
                ? NowUnix() + CooldownSeconds
                : Math.Max(_activeCooldownUntilUnix, _cachedState?.CooldownUntilUnix ?? 0);

            if (cooldown > 0 && cooldown <= NowUnix())
                cooldown = 0;

            _activeCooldownUntilUnix = cooldown;

            _cachedState = new SavedAccountState
            {
                LicenseKey = licenseKey,
                SteamId = account.SteamId,
                Username = account.Username,
                FullKey = account.FullKey,
                CooldownUntilUnix = cooldown
            };

            return WriteStateToDisk(_cachedState);
        }

        public static void Clear()
        {
            _activeCooldownUntilUnix = 0;
            _cachedState = null;
            SecureLocalStore.Delete(StatePath);
        }

        private static SteamAccount BuildAccountFromSavedState(SavedAccountState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.FullKey))
                return null;

            Program.ParseAccountsFromText(state.FullKey);
            var parsed = Program.accounts.FirstOrDefault(a => a != null && a.IsValid);
            if (parsed == null)
                return null;

            if (!string.IsNullOrEmpty(state.SteamId))
                parsed.SteamId = state.SteamId;
            if (!string.IsNullOrEmpty(state.Username))
                parsed.Username = state.Username;
            parsed.FullKey = state.FullKey;
            return parsed;
        }

        private static long LoadCooldownUntilFromDisk()
        {
            var state = ReadStateFromDisk();
            if (state == null || state.CooldownUntilUnix <= 0)
                return 0;
            if (NowUnix() >= state.CooldownUntilUnix)
                return 0;
            return state.CooldownUntilUnix;
        }

        private static SavedAccountState ReadStateFromDisk()
        {
            try
            {
                string raw = SecureLocalStore.ReadTextAndUpgrade(StatePath);
                if (string.IsNullOrEmpty(raw))
                    return null;

                var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 4)
                    return null;

                long cooldown = 0;
                long.TryParse(lines[3].Trim(), out cooldown);

                string fullKey = "";
                if (lines.Length >= 5 && !string.IsNullOrWhiteSpace(lines[4]))
                {
                    try { fullKey = Encoding.UTF8.GetString(Convert.FromBase64String(lines[4].Trim())); }
                    catch { fullKey = lines[4].Trim(); }
                }

                return new SavedAccountState
                {
                    LicenseKey = NormalizeKey(lines[0]),
                    SteamId = (lines[1] ?? "").Trim(),
                    Username = (lines[2] ?? "").Trim(),
                    CooldownUntilUnix = cooldown,
                    FullKey = fullKey
                };
            }
            catch { return null; }
        }

        private static bool WriteStateToDisk(SavedAccountState state)
        {
            if (state == null)
                return false;

            try
            {
                string fullKeyB64 = string.IsNullOrEmpty(state.FullKey)
                    ? ""
                    : Convert.ToBase64String(Encoding.UTF8.GetBytes(state.FullKey));

                var text = string.Join(Environment.NewLine, new[]
                {
                    state.LicenseKey ?? "",
                    state.SteamId ?? "",
                    state.Username ?? "",
                    state.CooldownUntilUnix.ToString(),
                    fullKeyB64
                });

                return SecureLocalStore.WriteText(StatePath, text);
            }
            catch
            {
                return false;
            }
        }

        private static long NowUnix()
            => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "LOCAL";
            value = value.Trim();
            int colon = value.IndexOf(':');
            if (colon > 0)
                value = value.Substring(0, colon);
            return value.Trim().ToUpperInvariant();
        }
    }
}

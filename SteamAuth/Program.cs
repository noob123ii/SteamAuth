using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SyntaX
{
    internal static class AntiTamper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryInformationProcess(IntPtr hProcess, int infoClass,
            ref IntPtr info, int infoLength, IntPtr returnLength);

        private static readonly string[] BlacklistedProcesses =
        {
            "dnspy", "dnspy.console", "x64dbg", "x32dbg", "ollydbg", "ida", "ida64", "idag", "idag64",
            "idaw", "idaw64", "idaq", "idaq64", "immunitydebugger", "windbg", "processhacker",
            "scylla", "scylla_x64", "scylla_x86", "peid", "de4dot", "ilspy", "dotpeek", "reflector",
            "justdecompile", "megadumper", "extremedumper", "codecracker", "simpleassembly",
            "cheatengine-x86_64", "cheatengine-i386",
            "fiddler", "fiddler everywhere", "fiddlereverywhere", "progress telerik fiddler",
            "httpdebugger", "httpdebuggerui", "httpdebuggerpro", "httpanalyzerstdv7",
            "charles", "charles64", "charles proxy",
            "burpsuite", "burpsuite_pro", "burpsuite_community", "burp",
            "mitmproxy", "mitmdump", "mitmweb", "mitm",
            "httptoolkit", "httptoolkit-server", "httptoolkit-desktop",
            "wireshark", "dumpcap", "tshark", "rawcap",
            "proxifier", "proxycap", "proxyman",
            "zaproxy", "owaspzap", "zap",
            "smartsniff", "networkminer", "commview"
        };

        private static readonly string[] BlacklistedProcessFragments =
        {
            "fiddler", "burp", "charles", "mitm", "wireshark", "httpdebug", "httptoolkit",
            "proxifier", "zaproxy", "httpanalyzer", "packetmon", "smartsniff"
        };

        private static volatile bool _running = true;

        public static void Initialize()
        {
            RunChecks();
            var t = new Thread(WatchdogLoop) { IsBackground = true, Name = "wd" };
            t.Start();
        }

        private static void WatchdogLoop()
        {
            while (_running)
            {
                try
                {
                    RunChecks();
                    Thread.Sleep(1500);
                }
                catch
                {
                    Die();
                }
            }
        }

        private static void RunChecks()
        {
            if (Debugger.IsAttached || Debugger.IsLogging())
                Die();

            try { if (IsDebuggerPresent()) Die(); } catch { }

            try
            {
                bool remote = false;
                if (CheckRemoteDebuggerPresent(GetCurrentProcess(), ref remote) && remote)
                    Die();
            }
            catch { }

            try
            {
                IntPtr port = IntPtr.Zero;
                // ProcessDebugPort = 7
                if (NtQueryInformationProcess(GetCurrentProcess(), 7, ref port, IntPtr.Size, IntPtr.Zero) == 0
                    && port != IntPtr.Zero)
                    Die();
            }
            catch { }

            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    string name;
                    try { name = p.ProcessName.ToLowerInvariant(); }
                    catch { continue; }

                    for (int i = 0; i < BlacklistedProcesses.Length; i++)
                    {
                        if (name == BlacklistedProcesses[i])
                            Die();
                    }

                    for (int i = 0; i < BlacklistedProcessFragments.Length; i++)
                    {
                        if (name.IndexOf(BlacklistedProcessFragments[i], StringComparison.Ordinal) >= 0)
                            Die();
                    }
                }
            }
            catch { }

            try { NetworkGuard.RunChecks(); } catch { Die(); }

            // Timing check: a debugger stepping through this block will blow the budget.
            try
            {
                var sw = Stopwatch.StartNew();
                int x = 0;
                for (int i = 0; i < 1000; i++) x ^= i;
                sw.Stop();
                if (sw.ElapsedMilliseconds > 250) Die();
                GC.KeepAlive(x);
            }
            catch { }
        }

        private static void Die()
        {
            _running = false;
            try { Environment.FailFast(null); } catch { }
            Process.GetCurrentProcess().Kill();
        }

        internal static void Trigger() => Die();
    }

    internal class SteamAccount
    {
        public int Number { get; set; }
        public string Username { get; set; }
        public string FullKey { get; set; }
        public string TokenPart { get; set; }
        public string SteamId { get; set; }
        public int Steam32Id { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal class Program
    {
        internal static List<SteamAccount> accounts = new List<SteamAccount>();
        private static int validCount = 0;
        private static int invalidCount = 0;

        private static string FindSteamPathViaRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath) && File.Exists(Path.Combine(steamPath, "Steam.exe")))
                            return steamPath;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool ValidateTokenFormat(string token)
        {
            const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.=";
            return !string.IsNullOrEmpty(token) && token.All(ch => validChars.Contains(ch));
        }

        private static JwtSecurityToken DecodeToken(string tokenPart, out string error)
        {
            error = null;

            if (!ValidateTokenFormat(tokenPart))
            {
                error = "Invalid token format (contains illegal characters)";
                return null;
            }

            int dotCount = tokenPart.Count(c => c == '.');
            if (dotCount < 2)
            {
                error = $"JWT incomplete: has {dotCount} dots, needs 2+";
                return null;
            }

            var handler = new JwtSecurityTokenHandler();
            try
            {
                return handler.ReadJwtToken(tokenPart);
            }
            catch (Exception e)
            {
                error = $"Decode failed: {e.Message}";
                return null;
            }
        }

        private static void ExitAfterEnter(string message)
        {
            Console.WriteLine(message);
            Console.ReadLine();
            Environment.Exit(1);
        }

        private static string ReadAccountsFromFile(string filename = "1.txt")
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);

            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"⚠️ File not found: {fullPath}");
                Console.WriteLine("Looking in current directory...");
                fullPath = Path.Combine(Directory.GetCurrentDirectory(), filename);

                if (!File.Exists(fullPath))
                {
                    Console.WriteLine($"❌ Could not find {filename} anywhere!");
                    return null;
                }
            }

            Console.WriteLine($"✅ Found file: {fullPath}");

            try
            {
                return File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading file: {ex.Message}");
                return null;
            }
        }

        internal static void ParseAccountsFromText(string text)
        {
            accounts.Clear();
            validCount = 0;
            invalidCount = 0;

            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int accountNumber = 1;

            Console.WriteLine();
            Console.WriteLine("🔍 PARSING ACCOUNTS...");
            Console.WriteLine(new string('─', 60));

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                var account = new SteamAccount
                {
                    Number = accountNumber,
                    FullKey = trimmedLine,
                    IsValid = false
                };

                // Check if line contains a dot (JWT format)
                if (!trimmedLine.Contains('.'))
                {
                    account.IsValid = false;
                    account.ErrorMessage = "No dots found (invalid JWT format)";
                    invalidCount++;
                    accounts.Add(account);
                    accountNumber++;
                    Console.WriteLine($"  ❌ [#{account.Number - 1}] SKIPPED - {account.ErrorMessage}");
                    continue;
                }

                int firstDotIndex = trimmedLine.IndexOf('.');
                if (firstDotIndex <= 0)
                {
                    account.IsValid = false;
                    account.ErrorMessage = "Username missing before JWT";
                    invalidCount++;
                    accounts.Add(account);
                    accountNumber++;
                    Console.WriteLine($"  ❌ [#{account.Number - 1}] SKIPPED - {account.ErrorMessage}");
                    continue;
                }

                account.Username = trimmedLine.Substring(0, firstDotIndex);
                account.TokenPart = trimmedLine.Substring(firstDotIndex + 1);

                // Validate token part has 3 sections
                int dotsInToken = account.TokenPart.Count(c => c == '.');
                if (dotsInToken != 2)
                {
                    account.IsValid = false;
                    account.ErrorMessage = $"Token has {dotsInToken} parts (needs 2 dots for header.payload.signature)";
                    invalidCount++;
                    accounts.Add(account);
                    accountNumber++;
                    Console.WriteLine($"  ❌ [#{account.Number - 1}] {account.Username} - {account.ErrorMessage}");
                    continue;
                }

                // Try to decode token
                JwtSecurityToken decoded = DecodeToken(account.TokenPart, out string decodeError);
                if (decoded == null)
                {
                    account.IsValid = false;
                    account.ErrorMessage = decodeError;
                    invalidCount++;
                    accounts.Add(account);
                    accountNumber++;
                    Console.WriteLine($"  ❌ [#{account.Number - 1}] {account.Username} - {account.ErrorMessage}");
                    continue;
                }

                // Get Steam ID
                account.SteamId = decoded.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                if (string.IsNullOrEmpty(account.SteamId))
                {
                    account.IsValid = false;
                    account.ErrorMessage = "No 'sub' claim (Steam ID missing)";
                    invalidCount++;
                    accounts.Add(account);
                    accountNumber++;
                    Console.WriteLine($"  ❌ [#{account.Number - 1}] {account.Username} - {account.ErrorMessage}");
                    continue;
                }

                // Calculate Steam32 ID
                try
                {
                    account.Steam32Id = new SteamID(account.SteamId).GetSteam32Id();
                    account.IsValid = true;
                    validCount++;
                    Console.WriteLine($"  ✅ [#{account.Number}] {account.Username} - Valid (Steam32: {account.Steam32Id})");
                }
                catch (Exception ex)
                {
                    account.IsValid = false;
                    account.ErrorMessage = $"Failed to get Steam32 ID: {ex.Message}";
                    invalidCount++;
                    Console.WriteLine($"  ❌ [#{account.Number}] {account.Username} - {account.ErrorMessage}");
                }

                accounts.Add(account);
                accountNumber++;
            }

            Console.WriteLine(new string('─', 60));
            Console.WriteLine($"📊 SUMMARY: {validCount} Valid | {invalidCount} Invalid | Total: {accounts.Count}");
            Console.WriteLine();
        }

        private static void DisplayAccounts()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                           SyntaX                                ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"📊 You Have: {validCount} Valid Accounts / {invalidCount} Invalid (Skipped)");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine();

            var validAccounts = accounts.Where(a => a.IsValid).ToList();

            if (validAccounts.Count == 0)
            {
                Console.WriteLine("  ⚠️ No valid accounts found!");
                Console.WriteLine();
                Console.WriteLine("  Invalid accounts:");
                foreach (var acc in accounts.Where(a => !a.IsValid))
                {
                    Console.WriteLine($"    ❌ {acc.ErrorMessage}");
                }
                Console.WriteLine();
                Console.WriteLine("  Press Enter to exit...");
                Console.ReadLine();
                Environment.Exit(0);
            }

            foreach (var acc in validAccounts)
            {
                Console.WriteLine($"  [{acc.Number}] {acc.Username}");
                Console.WriteLine($"      Steam ID: {acc.SteamId}");
                Console.WriteLine($"      Steam32:  {acc.Steam32Id}");
                Console.WriteLine();
            }
            Console.WriteLine(new string('─', 60));
        }

        private static void DisplayAccountSelection()
        {
            var validAccounts = accounts.Where(a => a.IsValid).ToList();

            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                      SELECT AN ACCOUNT                         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"  Available valid accounts: {validAccounts.Count}");
            Console.WriteLine();
            Console.WriteLine($"  Enter account number (1-{validAccounts.Count}) or:");
            Console.WriteLine("  [S] Scan all accounts for games (Gorilla Tag + expensive games)");
            Console.WriteLine("  [F] Find account by Steam ID (64-bit)");
            Console.WriteLine("  [R] Refresh/Reload from 1.txt");
            Console.WriteLine("  [Q] Quit");
            Console.WriteLine();
            Console.Write("  ➤ Your choice: ");
        }

        private static void ReloadAccounts()
        {
            Console.Clear();
            Console.WriteLine("🔄 RELOADING ACCOUNTS FROM 1.txt...");
            string accountData = ReadAccountsFromFile("1.txt");

            if (string.IsNullOrEmpty(accountData))
            {
                Console.WriteLine("❌ Failed to load accounts from 1.txt");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                Environment.Exit(1);
            }

            ParseAccountsFromText(accountData);

            if (accounts.Where(a => a.IsValid).Count() == 0)
            {
                Console.WriteLine("❌ No valid accounts found in file!");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                Environment.Exit(1);
            }

            DisplayAccounts();
        }

        private static void FindBySteamId(string query = null)
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    FIND ACCOUNT BY STEAM ID                     ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            if (string.IsNullOrWhiteSpace(query))
            {
                Console.Write("  Enter Steam ID (64-bit, e.g. 76561198xxxxxxxxx) or Steam32 ID: ");
                query = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("  ⚠️ No input provided.");
                Console.Write("  Press Enter to return...");
                Console.ReadLine();
                return;
            }

            query = query.Trim();
            // Allow pasting a full profile URL
            var urlMatch = Regex.Match(query, @"(\d{17})");
            if (urlMatch.Success && query.Length > 17) query = urlMatch.Groups[1].Value;

            // Match against SteamId64, Steam32 string form, or substring of either
            var matches = accounts.Where(a =>
                (!string.IsNullOrEmpty(a.SteamId) && a.SteamId == query) ||
                (a.Steam32Id != 0 && a.Steam32Id.ToString() == query) ||
                (!string.IsNullOrEmpty(a.SteamId) && a.SteamId.Contains(query))
            ).ToList();

            Console.WriteLine();
            if (matches.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ❌ No account found matching '{query}'");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine();
                Console.WriteLine($"  Searched {accounts.Count} accounts ({accounts.Count(a => a.IsValid)} valid).");
                Console.WriteLine();
                Console.Write("  Press Enter to return...");
                Console.ReadLine();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ Found {matches.Count} match(es):");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(new string('─', 60));

            foreach (var acc in matches)
            {
                Console.WriteLine();
                Console.WriteLine($"  Username   : {acc.Username}");
                Console.WriteLine($"  Steam ID64 : {acc.SteamId}");
                Console.WriteLine($"  Steam32    : {acc.Steam32Id}");
                Console.WriteLine($"  Valid      : {(acc.IsValid ? "Yes" : "No - " + acc.ErrorMessage)}");
                Console.WriteLine($"  Line #     : {acc.Number}");
                Console.WriteLine($"  Profile    : https://steamcommunity.com/profiles/{acc.SteamId}");
                Console.WriteLine();
                Console.WriteLine("  Token (full key):");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  {acc.FullKey}");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(new string('─', 60));
            }

            Console.WriteLine();
            Console.WriteLine("  Options:");
            Console.WriteLine("    [L] Login to first match");
            Console.WriteLine("    [C] Copy first match's full key to clipboard");
            Console.WriteLine("    [Enter] Return to menu");
            Console.Write("  ➤ ");
            string choice = Console.ReadLine()?.Trim().ToUpperInvariant();

            var first = matches[0];
            if (choice == "L" && first.IsValid)
            {
                LoginToAccount(first);
                Console.WriteLine();
                Console.Write("  Press Enter to return...");
                Console.ReadLine();
            }
            else if (choice == "C")
            {
                try
                {
                    // Try clipboard via clip.exe (works without WinForms reference)
                    var psi = new ProcessStartInfo("cmd.exe", "/c clip")
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.StandardInput.Write(first.FullKey);
                        p.StandardInput.Close();
                        p.WaitForExit();
                    }
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✅ Copied to clipboard.");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Clipboard copy failed: {ex.Message}");
                }
                Console.Write("  Press Enter to return...");
                Console.ReadLine();
            }
        }

        private const int GorillaTagAppId = 1533390;

        // appid -> (name, approx USD price)
        private static readonly Dictionary<int, Tuple<string, decimal>> ExpensiveGames =
            new Dictionary<int, Tuple<string, decimal>>
        {
            { 1716740, Tuple.Create("Starfield", 69.99m) },
            { 2050650, Tuple.Create("Resident Evil 4 (2023)", 59.99m) },
            { 1888930, Tuple.Create("The Last of Us Part I", 59.99m) },
            { 1817070, Tuple.Create("Marvel's Spider-Man Remastered", 59.99m) },
            { 1817190, Tuple.Create("Marvel's Spider-Man: Miles Morales", 49.99m) },
            { 1245620, Tuple.Create("ELDEN RING", 59.99m) },
            { 2622380, Tuple.Create("ELDEN RING NIGHTREIGN", 39.99m) },
            { 1086940, Tuple.Create("Baldur's Gate 3", 59.99m) },
            { 1174180, Tuple.Create("Red Dead Redemption 2", 59.99m) },
            { 1091500, Tuple.Create("Cyberpunk 2077", 59.99m) },
            { 2138330, Tuple.Create("Cyberpunk 2077: Phantom Liberty", 29.99m) },
            { 1551360, Tuple.Create("Forza Horizon 5", 59.99m) },
            { 990080,  Tuple.Create("Hogwarts Legacy", 59.99m) },
            { 1238840, Tuple.Create("Battlefield 2042", 59.99m) },
            { 1985810, Tuple.Create("Skull and Bones", 69.99m) },
            { 1812620, Tuple.Create("Atomic Heart", 59.99m) },
            { 489830,  Tuple.Create("Skyrim SE", 39.99m) },
            { 377160,  Tuple.Create("Fallout 4", 19.99m) },
            { 252490,  Tuple.Create("Rust", 39.99m) },
            { 271590,  Tuple.Create("GTA V", 29.99m) },
            { 3240220, Tuple.Create("GTA V Enhanced", 19.99m) },
            { 292030,  Tuple.Create("The Witcher 3", 39.99m) },
            { 374320,  Tuple.Create("DARK SOULS III", 59.99m) },
            { 814380,  Tuple.Create("Sekiro: Shadows Die Twice", 59.99m) },
            { 1599660, Tuple.Create("Stray", 29.99m) },
            { 1426210, Tuple.Create("It Takes Two", 39.99m) },
            { 1145360, Tuple.Create("Hades", 24.99m) },
            { 1145350, Tuple.Create("Hades II", 29.99m) },
            { 632360,  Tuple.Create("Risk of Rain 2", 24.99m) },
            { 1222670, Tuple.Create("The Sims 4 (paid bundles)", 39.99m) },
            { 8930,    Tuple.Create("Sid Meier's Civilization V", 29.99m) },
            { 289070,  Tuple.Create("Sid Meier's Civilization VI", 59.99m) },
            { 1158310, Tuple.Create("Crusader Kings III", 49.99m) },
            { 394360,  Tuple.Create("Hearts of Iron IV", 39.99m) },
            { 236850,  Tuple.Create("Europa Universalis IV", 39.99m) },
            { 281990,  Tuple.Create("Stellaris", 39.99m) },
            { 1222730, Tuple.Create("STAR WARS Jedi: Survivor", 69.99m) },
            { 1774580, Tuple.Create("STAR WARS Jedi: Fallen Order", 39.99m) },
            { 2344520, Tuple.Create("Diablo IV", 69.99m) },
            { 1599340, Tuple.Create("Lost Ark", 0m) }, // free, exclude price-wise
            { 1623730, Tuple.Create("Palworld", 29.99m) },
            { 1063730, Tuple.Create("New World", 39.99m) },
            { 8500,    Tuple.Create("EVE Online", 0m) },
            { 813780,  Tuple.Create("Age of Empires II: Definitive Edition", 19.99m) },
            { 933110,  Tuple.Create("Age of Empires IV", 59.99m) },
            { 1599020, Tuple.Create("FIFA 23", 59.99m) },
            { 2195250, Tuple.Create("EA SPORTS FC 24", 69.99m) },
            { 2669320, Tuple.Create("EA SPORTS FC 25", 69.99m) },
            { 242760,  Tuple.Create("The Forest", 19.99m) },
            { 526870,  Tuple.Create("Satisfactory", 39.99m) },
            { 1142710, Tuple.Create("Total War: WARHAMMER III", 59.99m) },
            { 2767030, Tuple.Create("Marvel Rivals", 0m) },
            { 1888160, Tuple.Create("ARK: Survival Ascended", 44.99m) },
            { 346110,  Tuple.Create("ARK: Survival Evolved", 29.99m) },
            { 1659040, Tuple.Create("HELLDIVERS 2", 39.99m) },
            { 553850,  Tuple.Create("HELLDIVERS", 19.99m) },
            { 1604030, Tuple.Create("Tarisland", 0m) },
            { 870780,  Tuple.Create("Control Ultimate Edition", 29.99m) },
            { 1182900, Tuple.Create("A Plague Tale: Requiem", 49.99m) },
            { 752590,  Tuple.Create("A Plague Tale: Innocence", 39.99m) },
            { 1426110, Tuple.Create("Black Myth: Wukong", 59.99m) },
            { 1361210, Tuple.Create("Monster Hunter Rise", 39.99m) },
            { 582010,  Tuple.Create("Monster Hunter: World", 29.99m) },
            { 2246340, Tuple.Create("Monster Hunter Wilds", 69.99m) },
            { 1240440, Tuple.Create("HALO Infinite", 59.99m) },
            { 976730,  Tuple.Create("Halo: The Master Chief Collection", 39.99m) },
            { 736260,  Tuple.Create("Baldur's Gate Enhanced Edition", 19.99m) },
            { 2073850, Tuple.Create("THE FINALS", 0m) },
            { 359550,  Tuple.Create("Rainbow Six Siege", 19.99m) },
            { 1782210, Tuple.Create("Crab Game", 0m) },
            { 1568400, Tuple.Create("Sifu", 39.99m) },
        };

        // Uses the account's own JWT access_token to call IPlayerService/GetOwnedGames.
        // Bypasses privacy settings because we are authenticating AS that user.
        private static Dictionary<int, double> FetchOwnedGamesFromWebApi(SteamAccount acc, out string status)
        {
            status = null;
            try
            {
                if (string.IsNullOrEmpty(acc.TokenPart) || string.IsNullOrEmpty(acc.SteamId))
                {
                    status = "WebAPI: missing token or steamid";
                    return null;
                }

                string token = Uri.EscapeDataString(acc.TokenPart);
                string url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
                             + $"?access_token={token}"
                             + $"&steamid={acc.SteamId}"
                             + "&include_appinfo=false"
                             + "&include_played_free_games=true"
                             + "&skip_unvetted_apps=false";

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] = "Valve/Steam SyntaXScanner";
                    string json = wc.DownloadString(url);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        status = "WebAPI empty response";
                        return null;
                    }

                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (!doc.RootElement.TryGetProperty("response", out var resp))
                        {
                            status = "WebAPI: no 'response' field";
                            return null;
                        }

                        var result = new Dictionary<int, double>();

                        if (!resp.TryGetProperty("games", out var games))
                        {
                            if (resp.TryGetProperty("game_count", out var gc) && gc.GetInt32() == 0)
                            {
                                status = "OK (0 games via WebAPI)";
                                return result;
                            }
                            // Empty response usually means token rejected
                            status = "WebAPI: token rejected or no permission (empty response)";
                            return null;
                        }

                        foreach (var g in games.EnumerateArray())
                        {
                            if (!g.TryGetProperty("appid", out var aidEl)) continue;
                            int appid = aidEl.GetInt32();
                            double mins = 0;
                            if (g.TryGetProperty("playtime_forever", out var ptEl))
                            {
                                try { mins = ptEl.GetDouble(); }
                                catch { mins = 0; }
                            }
                            result[appid] = mins / 60.0;
                        }

                        status = $"OK ({result.Count} games via WebAPI)";
                        return result;
                    }
                }
            }
            catch (WebException wex)
            {
                var hresp = wex.Response as HttpWebResponse;
                if (hresp != null)
                {
                    int code = (int)hresp.StatusCode;
                    if (code == 401 || code == 403)
                        status = $"WebAPI: token unauthorized/expired (HTTP {code})";
                    else
                        status = $"WebAPI HTTP {code}: {wex.Message}";
                }
                else
                {
                    status = "WebAPI HTTP error: " + wex.Message;
                }
                return null;
            }
            catch (Exception ex)
            {
                status = "WebAPI error: " + ex.Message;
                return null;
            }
        }

        private static string DownloadWithBrowserHeaders(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            req.Headers["Accept-Language"] = "en-US,en;q=0.9";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var sr = new StreamReader(stream, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // SteamDB calculator fallback. Best-effort: extracts appids and playtime when available.
        private static Dictionary<int, double> FetchOwnedGamesFromSteamDB(string steamId64, out string status)
        {
            status = null;
            try
            {
                string url = $"https://steamdb.info/calculator/?player={steamId64}&all_games";
                string html = DownloadWithBrowserHeaders(url);

                if (string.IsNullOrWhiteSpace(html))
                {
                    status = "SteamDB empty response";
                    return null;
                }

                bool looksBlocked = (html.IndexOf("Just a moment", StringComparison.OrdinalIgnoreCase) >= 0
                                     || html.IndexOf("cf-browser-verification", StringComparison.OrdinalIgnoreCase) >= 0
                                     || html.IndexOf("Attention Required", StringComparison.OrdinalIgnoreCase) >= 0)
                                    && html.IndexOf("data-appid=", StringComparison.OrdinalIgnoreCase) < 0;
                if (looksBlocked)
                {
                    status = "SteamDB Cloudflare-blocked";
                    return null;
                }

                var rows = Regex.Matches(html,
                    @"<tr[^>]*data-appid=""(\d+)""[^>]*>(.*?)</tr>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (rows.Count == 0)
                {
                    // SteamDB shows a "private profile" notice if the games list is hidden
                    if (html.IndexOf("private", StringComparison.OrdinalIgnoreCase) >= 0)
                        status = "SteamDB: profile private";
                    else
                        status = "SteamDB: no games rows parsed";
                    return null;
                }

                var result = new Dictionary<int, double>();
                foreach (Match row in rows)
                {
                    if (!int.TryParse(row.Groups[1].Value, out int appId) || appId <= 0) continue;
                    string body = row.Groups[2].Value;
                    double hours = 0;

                    var p1 = Regex.Match(body, @">\s*(\d+)\s*h\s*(\d+)\s*m\s*<", RegexOptions.IgnoreCase);
                    if (p1.Success)
                    {
                        double.TryParse(p1.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double h);
                        double.TryParse(p1.Groups[2].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double m);
                        hours = h + m / 60.0;
                    }
                    else
                    {
                        var p2 = Regex.Match(body, @">\s*(\d+(?:\.\d+)?)\s*hours?\s*<", RegexOptions.IgnoreCase);
                        if (p2.Success)
                        {
                            double.TryParse(p2.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out hours);
                        }
                        else
                        {
                            var p3 = Regex.Match(body, @">\s*(\d+)\s*minutes?\s*<", RegexOptions.IgnoreCase);
                            if (p3.Success && int.TryParse(p3.Groups[1].Value, out int mins))
                                hours = mins / 60.0;
                        }
                    }

                    result[appId] = hours;
                }

                status = $"OK ({result.Count} games via SteamDB)";
                return result;
            }
            catch (WebException wex)
            {
                var hresp = wex.Response as HttpWebResponse;
                if (hresp != null && (int)hresp.StatusCode == 403)
                    status = "SteamDB Cloudflare-blocked (HTTP 403)";
                else
                    status = "SteamDB HTTP error: " + wex.Message;
                return null;
            }
            catch (Exception ex)
            {
                status = "SteamDB error: " + ex.Message;
                return null;
            }
        }

        // Returns appid -> hoursOnRecord (0 if absent / never played)
        private static Dictionary<int, double> FetchOwnedGames(string steamId64, out string status)
        {
            status = null;
            try
            {
                string url = $"https://steamcommunity.com/profiles/{steamId64}/games?tab=all&xml=1";
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 SyntaXScanner";
                    string xml = wc.DownloadString(url);

                    if (string.IsNullOrWhiteSpace(xml))
                    {
                        status = "Empty response";
                        return null;
                    }

                    if (xml.IndexOf("<error>", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = Regex.Match(xml, @"<error>\s*(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?\s*</error>",
                            RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        string err = m.Success ? m.Groups[1].Value.Trim() : "Profile error";
                        if (err.IndexOf("private", StringComparison.OrdinalIgnoreCase) >= 0)
                            status = "Games list private or hidden";
                        else
                            status = err;
                        return null;
                    }

                    if (xml.IndexOf("<games", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        status = "Games list private or hidden";
                        return null;
                    }

                    // Iterate <game>...</game> blocks; tolerant of unescaped '&' since we use regex.
                    var result = new Dictionary<int, double>();
                    var gameMatches = Regex.Matches(xml, @"<game>(.*?)</game>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    foreach (Match gm in gameMatches)
                    {
                        string block = gm.Groups[1].Value;
                        var idM = Regex.Match(block, @"<appID>\s*(\d+)\s*</appID>", RegexOptions.IgnoreCase);
                        if (!idM.Success) continue;
                        if (!int.TryParse(idM.Groups[1].Value, out int appId) || appId <= 0) continue;

                        double hours = 0;
                        var hM = Regex.Match(block, @"<hoursOnRecord>\s*([\d.,]+)\s*</hoursOnRecord>",
                            RegexOptions.IgnoreCase);
                        if (hM.Success)
                        {
                            string raw = hM.Groups[1].Value.Replace(",", "");
                            double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out hours);
                        }

                        result[appId] = hours;
                    }

                    // Fallback if Steam ever drops the wrapper, still grab any loose appIDs
                    if (result.Count == 0)
                    {
                        var loose = Regex.Matches(xml, @"<appID>\s*(\d+)\s*</appID>", RegexOptions.IgnoreCase);
                        foreach (Match m2 in loose)
                            if (int.TryParse(m2.Groups[1].Value, out int id) && id > 0)
                                result[id] = 0;
                    }

                    status = $"OK ({result.Count} games)";
                    return result;
                }
            }
            catch (WebException wex)
            {
                status = "HTTP error: " + wex.Message;
                return null;
            }
            catch (Exception ex)
            {
                status = "Error: " + ex.Message;
                return null;
            }
        }

        private static void ScanAllAccountsForGames()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                  SCANNING ACCOUNTS FOR GAMES                    ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            var validAccounts = accounts.Where(a => a.IsValid).ToList();
            if (validAccounts.Count == 0)
            {
                Console.WriteLine("  ⚠️ No valid accounts to scan.");
                Console.WriteLine();
                Console.Write("  Press Enter to return...");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"  Scanning {validAccounts.Count} account(s) via public Steam Community XML...");
            Console.WriteLine($"  (Profiles must have public game lists)");
            Console.WriteLine(new string('─', 60));

            int gorillaTagCount = 0;
            int privateCount = 0;
            int errorCount = 0;
            int publicScanned = 0;
            var gorillaOwners = new List<Tuple<string, double>>();
            var expensiveOwnersByGame = new Dictionary<int, List<string>>();
            var perAccountExpensive = new Dictionary<string, List<string>>();

            for (int i = 0; i < validAccounts.Count; i++)
            {
                var acc = validAccounts[i];
                Console.Write($"  [{i + 1}/{validAccounts.Count}] {acc.Username} ({acc.SteamId})... ");

                // Primary: authenticated Web API call using this account's own JWT.
                var games = FetchOwnedGamesFromWebApi(acc, out string status);
                string source = "WebAPI";

                // Fallback 1: public community XML.
                if (games == null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("(WebAPI failed, trying XML) ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    var xmlGames = FetchOwnedGames(acc.SteamId, out string xmlStatus);
                    if (xmlGames != null)
                    {
                        games = xmlGames;
                        status = xmlStatus;
                        source = "XML";
                    }
                    else
                    {
                        status = status + " | XML: " + xmlStatus;

                        // Fallback 2: SteamDB, only if XML said private.
                        if (xmlStatus != null && xmlStatus.IndexOf("private", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("(trying SteamDB) ");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            var sdbGames = FetchOwnedGamesFromSteamDB(acc.SteamId, out string sdbStatus);
                            if (sdbGames != null)
                            {
                                games = sdbGames;
                                status = sdbStatus;
                                source = "SteamDB";
                            }
                            else
                            {
                                status = status + " | " + sdbStatus;
                            }
                        }
                    }
                }

                if (games == null)
                {
                    if (status != null && status.IndexOf("private", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        privateCount++;
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"🔒 {status}");
                    }
                    else
                    {
                        errorCount++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ {status}");
                    }
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Threading.Thread.Sleep(400);
                    continue;
                }

                publicScanned++;

                double totalHours = games.Values.Sum();
                double gorillaHours = games.TryGetValue(GorillaTagAppId, out double gh) ? gh : -1;
                bool hasGorilla = gorillaHours >= 0;

                if (hasGorilla)
                {
                    gorillaTagCount++;
                    gorillaOwners.Add(Tuple.Create($"{acc.Username} ({acc.SteamId})", gorillaHours));
                }

                var ownedExpensive = new List<string>();
                foreach (var kv in games)
                {
                    int id = kv.Key;
                    double hours = kv.Value;
                    if (ExpensiveGames.TryGetValue(id, out var info) && info.Item2 >= 20m)
                    {
                        ownedExpensive.Add($"{info.Item1} (~${info.Item2}) — {hours:0.#}h");
                        if (!expensiveOwnersByGame.ContainsKey(id))
                            expensiveOwnersByGame[id] = new List<string>();
                        expensiveOwnersByGame[id].Add($"{acc.Username} ({hours:0.#}h)");
                    }
                }

                if (ownedExpensive.Count > 0)
                    perAccountExpensive[$"{acc.Username} ({acc.SteamId}) [total {totalHours:0}h]"] = ownedExpensive;

                Console.ForegroundColor = hasGorilla ? ConsoleColor.Green : ConsoleColor.Gray;
                string gorillaTxt = hasGorilla ? $" | 🦍 GORILLA TAG ({gorillaHours:0.#}h)" : "";
                string expTxt = ownedExpensive.Count > 0 ? $" | 💰 {ownedExpensive.Count} expensive" : "";
                Console.WriteLine($"✅ {games.Count} games [{source}] | ⏱ {totalHours:0}h total{gorillaTxt}{expTxt}");
                Console.ForegroundColor = ConsoleColor.Cyan;

                System.Threading.Thread.Sleep(500); // be polite to Steam
            }

            Console.WriteLine(new string('─', 60));
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  CONFIRMED HAS GORILLA TAG: {gorillaTagCount}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Public profiles scanned : {publicScanned}");
            Console.WriteLine($"  Private/hidden          : {privateCount}");
            Console.WriteLine($"  Errors                  : {errorCount}");
            Console.WriteLine();

            if (gorillaOwners.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  🦍 Gorilla Tag owners (sorted by playtime):");
                Console.ForegroundColor = ConsoleColor.Cyan;
                foreach (var o in gorillaOwners.OrderByDescending(t => t.Item2))
                    Console.WriteLine($"     - {o.Item1} — {o.Item2:0.#}h");
                Console.WriteLine();
            }

            if (perAccountExpensive.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  💰 Accounts with expensive games:");
                Console.ForegroundColor = ConsoleColor.Cyan;
                foreach (var kv in perAccountExpensive.OrderByDescending(k => k.Value.Count))
                {
                    Console.WriteLine($"     • {kv.Key} — {kv.Value.Count} expensive game(s)");
                    foreach (var g in kv.Value)
                        Console.WriteLine($"         - {g}");
                }
                Console.WriteLine();
            }

            // Save report
            try
            {
                string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scan_report.txt");
                var sb = new StringBuilder();
                sb.AppendLine("Steam Account Scan Report");
                sb.AppendLine("Generated: " + DateTime.Now);
                sb.AppendLine(new string('=', 60));
                sb.AppendLine($"CONFIRMED HAS GORILLA TAG: {gorillaTagCount}");
                sb.AppendLine($"Public scanned: {publicScanned} | Private: {privateCount} | Errors: {errorCount}");
                sb.AppendLine();
                sb.AppendLine("Gorilla Tag owners (sorted by playtime):");
                foreach (var o in gorillaOwners.OrderByDescending(t => t.Item2))
                    sb.AppendLine($"  - {o.Item1} — {o.Item2:0.#}h");
                sb.AppendLine();
                sb.AppendLine("Expensive games per account:");
                foreach (var kv in perAccountExpensive.OrderByDescending(k => k.Value.Count))
                {
                    sb.AppendLine("  " + kv.Key + " (" + kv.Value.Count + ")");
                    foreach (var g in kv.Value) sb.AppendLine("    - " + g);
                }
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
                Console.WriteLine($"  📄 Report saved: {reportPath}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Could not save report: {ex.Message}");
            }

            Console.Write("  Press Enter to return to menu...");
            Console.ReadLine();
        }

        private static void KillSteamProcesses()
        {
            string[] processes = { "Steam.exe", "steamwebhelper.exe", "steamservice.exe" };
            foreach (string proc in processes)
            {
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c taskkill /f /im {proc}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var p = Process.Start(psi))
                        p?.WaitForExit();
                }
                catch { }
            }
            System.Threading.Thread.Sleep(2000);
        }

        private static uint Crc32(byte[] bytes)
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }

            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
                crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
            return crc ^ 0xFFFFFFFF;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
            ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct,
            int dwFlags, ref DATA_BLOB pDataOut);

        private static byte[] DpapiProtect(byte[] data, byte[] entropy)
        {
            var inBlob = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            var entBlob = new DATA_BLOB();

            try
            {
                inBlob.pbData = Marshal.AllocHGlobal(data.Length);
                inBlob.cbData = data.Length;
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);

                if (entropy != null && entropy.Length > 0)
                {
                    entBlob.pbData = Marshal.AllocHGlobal(entropy.Length);
                    entBlob.cbData = entropy.Length;
                    Marshal.Copy(entropy, 0, entBlob.pbData, entropy.Length);
                }

                if (!CryptProtectData(ref inBlob, null, ref entBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                    throw new InvalidOperationException($"CryptProtectData failed. Win32Error={Marshal.GetLastWin32Error()}");

                byte[] result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (entBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(outBlob.pbData);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        internal static void LoginToAccount(SteamAccount account)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (string.IsNullOrEmpty(account.Username)
                || string.IsNullOrEmpty(account.TokenPart)
                || string.IsNullOrEmpty(account.SteamId))
                throw new InvalidOperationException("Account is missing required login data.");

            string steamDir = FindSteamPathViaRegistry();
            if (steamDir == null)
                throw new InvalidOperationException("Steam not found. Please install or run Steam at least once.");

            if (!steamDir.EndsWith("\\"))
                steamDir += "\\";

            KillSteamProcesses();

            if (account.Steam32Id == 0 && !string.IsNullOrEmpty(account.SteamId))
            {
                account.Steam32Id = new SteamID(account.SteamId).GetSteam32Id();
            }

            if (account.Steam32Id == 0)
                throw new InvalidOperationException("Could not determine Steam32 ID from the account key.");

            string userdataConfigDir = $"{steamDir}userdata\\{account.Steam32Id}\\config";
            Directory.CreateDirectory(userdataConfigDir);
            File.WriteAllText(Path.Combine(userdataConfigDir, "localconfig.vdf"),
                "\"UserLocalConfigStore\"\n{\n    \"streaming_v2\"\n    {\n        \"EnableStreaming\"       \"0\"\n    }\n    \"friends\"\n    {\n        \"SignIntoFriends\"\t\t\"0\"\n    }\n}\n",
                Encoding.UTF8);

            string configDir = steamDir + "config";
            Directory.CreateDirectory(configDir);

            string configVdf = $"\"InstallConfigStore\"\n{{\n    \"Software\"\n    {{\n        \"Valve\"\n        {{\n            \"Steam\"\n            {{\n                \"Accounts\"\n                {{\n                    \"{account.Username}\"\n                    {{\n                        \"SteamID\"\t\t\"{account.SteamId}\"\n                    }}\n                }}\n            }}\n        }}\n    }}\n}}\n";
            File.WriteAllText(Path.Combine(configDir, "config.vdf"), configVdf, Encoding.UTF8);

            string loginUsersVdf = $"\"users\"\n{{\n    \"{account.SteamId}\"\n    {{\n        \"AccountName\"\t\t\"{account.Username}\"\n        \"PersonaName\"\t\t\"{account.Username}\"\n        \"RememberPassword\"\t\t\"1\"\n        \"WantsOfflineMode\"\t\t\"0\"\n        \"SkipOfflineModeWarning\"\t\t\"0\"\n        \"AllowAutoLogin\"\t\t\"0\"\n        \"MostRecent\"\t\t\"1\"\n        \"Timestamp\"\t\t\"{(int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\"\n    }}\n}}\n";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginUsersVdf, Encoding.UTF8);

            string localAppDataSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam");
            Directory.CreateDirectory(localAppDataSteam);

            byte[] tokenBytes = Encoding.UTF8.GetBytes(account.TokenPart);
            byte[] entropy = Encoding.UTF8.GetBytes(account.Username);
            byte[] protectedToken = DpapiProtect(tokenBytes, entropy);
            string pw = BitConverter.ToString(protectedToken).Replace("-", "").ToLowerInvariant();

            string hdr = Crc32(Encoding.UTF8.GetBytes(account.Username)).ToString("x") + "1";
            string localVdf = $"\"MachineUserConfigStore\"\n{{\n    \"Software\"\n    {{\n        \"Valve\"\n        {{\n            \"Steam\"\n            {{\n                \"ConnectCache\"\n                {{\n                    \"{hdr}\"\t\t\"{pw}\"\n                }}\n            }}\n        }}\n    }}\n}}\n";
            File.WriteAllText(Path.Combine(localAppDataSteam, "local.vdf"), localVdf, Encoding.UTF8);

            try
            {
                Process.Start(new ProcessStartInfo("steam://0") { UseShellExecute = true });
            }
            catch { }

        }

        private static void PrintBanner()
        {
            string[] banner = new[]
            {
                @"  ___ ____  ____      _    _____ _        _   _   _ _____ _   _ ",
                @" |_ _/ ___||  _ \    / \  | ____| |      / \ | | | |_   _| | | |",
                @"  | |\___ \| |_) |  / _ \ |  _| | |     / _ \| | | | | | | |_| |",
                @"  | | ___) |  _ <  / ___ \| |___| |___ / ___ \ |_| | | | |  _  |",
                @" |___|____/|_| \_\/_/   \_\_____|_____/_/   \_\___/  |_| |_| |_|"
            };
            foreach (string line in banner)
                Console.WriteLine(line);
            Console.WriteLine();
        }

        private static string TryBase64Decode(string input)
        {
            string s = input.Trim();
            // Support url-safe base64
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;
            }
            try
            {
                byte[] data = Convert.FromBase64String(s);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }

        // Console Main removed; entry point is now SyntaX.App.Main in Ui.cs.
    }

    // SteamID helper class (add this if missing)
    public class SteamID
    {
        private string steamId;

        public SteamID(string steamId)
        {
            this.steamId = steamId;
        }

        public int GetSteam32Id()
        {
            if (ulong.TryParse(steamId, out ulong id64))
            {
                return (int)(id64 & 0xFFFFFFFF);
            }
            return 0;
        }
    }
}
using System;
using System.IO;

namespace SyntaX
{
    internal static class RuntimeSecrets
    {
        internal static string RemoteDataUrl => "https://syntax.cfd/SyntaxAPI/PlsDontFindMe/9RN3V.json";
        internal static string RemoteAccountsUrl => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.txt");
        internal static string[] RemoteAccountsUrls => new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.txt")
        };
        internal static string RemoteAccessKey => "FNV9BB";
        internal static string WebsiteUrl => "https://syntax.cfd";
        internal static string HeaderName => "X-API-Key";
        internal static string UserAgent => "Mozilla/5.0";
        internal static string StoreUrl => "https://discord.gg/2RAazAMrnd";

        internal static string LocalAppFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ".sx-cache"
            );

        internal static string SessionFile => Path.Combine(LocalAppFolder, ".st1");
        internal static string SidecarFile => Path.Combine(LocalAppFolder, ".st2");

        internal static string[] PinnedCertThumbprints => new[]
        {
            "9A7DF7C6E5435FDA31244A659EFBC24F7AD90FBD",
            "2812348C903AC0B44C670B2F8AD7846023CB87D4"
        };

        internal const int SessionDays = 30;
    }
}
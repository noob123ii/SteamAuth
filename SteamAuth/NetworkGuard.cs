using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace SyntaX
{
    internal static class NetworkGuard
    {
        private static readonly string[] MitmCaMarkers =
        {
            "do_not_trust", "fiddler", "charles proxy", "portswigger", "burp",
            "mitmproxy", "httptoolkit", "proxyman", "zscaler", "telerik",
            "owasp", "superfish", "eavesdropper"
        };

        public static HttpClientHandler CreateSecureHandler()
        {
            return new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = ValidateServerCertificate
            };
        }

        public static bool ValidateServerCertificate(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (certificate == null) return false;

            var pins = RuntimeSecrets.PinnedCertThumbprints;
            if (pins == null || pins.Length == 0) return false;

            for (int i = 0; i < pins.Length; i++)
            {
                if (string.IsNullOrEmpty(pins[i])) continue;
                if (ThumbprintMatches(certificate, pins[i])) return true;
            }

            if (chain?.ChainElements != null)
            {
                foreach (var element in chain.ChainElements)
                {
                    if (element?.Certificate == null) continue;
                    for (int i = 0; i < pins.Length; i++)
                    {
                        if (string.IsNullOrEmpty(pins[i])) continue;
                        if (ThumbprintMatches(element.Certificate, pins[i]))
                            return true;
                    }
                }
            }

            return false;
        }

        public static void RunChecks()
        {
            CheckSystemProxy();
            CheckMitmRootCertificates();
        }

        private static void CheckSystemProxy()
        {
            try
            {
                var target = new Uri(RuntimeSecrets.RemoteDataUrl);

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key != null)
                    {
                        if (key.GetValue("ProxyEnable") is int enabled && enabled == 1)
                            AntiTamper.Trigger();

                        string autoConfig = key.GetValue("AutoConfigURL") as string;
                        if (!string.IsNullOrWhiteSpace(autoConfig))
                            AntiTamper.Trigger();
                    }
                }

                var proxy = WebRequest.GetSystemWebProxy();
                if (proxy != null && !proxy.IsBypassed(target))
                {
                    var endpoint = proxy.GetProxy(target);
                    if (endpoint != null && !string.Equals(endpoint.Host, target.Host, StringComparison.OrdinalIgnoreCase))
                        AntiTamper.Trigger();
                }
            }
            catch { }
        }

        private static void CheckMitmRootCertificates()
        {
            try
            {
                CheckStore(StoreLocation.CurrentUser);
                CheckStore(StoreLocation.LocalMachine);
            }
            catch { }
        }

        private static void CheckStore(StoreLocation location)
        {
            using (var store = new X509Store(StoreName.Root, location))
            {
                store.Open(OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                {
                    if (LooksLikeMitmCa(cert))
                        AntiTamper.Trigger();
                }
            }
        }

        private static bool LooksLikeMitmCa(X509Certificate2 cert)
        {
            if (cert == null) return false;

            string blob = ((cert.Subject ?? "") + " " + (cert.Issuer ?? "") + " " + (cert.FriendlyName ?? ""))
                .ToLowerInvariant();

            for (int i = 0; i < MitmCaMarkers.Length; i++)
            {
                if (blob.IndexOf(MitmCaMarkers[i], StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        private static bool ThumbprintMatches(X509Certificate cert, string pinned)
        {
            if (cert == null) return false;
            var cert2 = cert as X509Certificate2 ?? new X509Certificate2(cert);
            string actual = (cert2.Thumbprint ?? "").Replace(" ", "").ToUpperInvariant();
            return actual.Length > 0 && string.Equals(actual, pinned, StringComparison.OrdinalIgnoreCase);
        }
    }
}

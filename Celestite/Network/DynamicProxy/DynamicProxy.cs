using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Celestite.Network.DynamicProxy.Windows;
using Celestite.Utils;

namespace Celestite.Network.DynamicProxy
{
    public sealed class DynamicProxyImpl : IWebProxy
    {
        private sealed record ProxySettings(WebProxy? Proxy, bool PassToGames);
        private static readonly string SettingsFile = Path.Combine(ConfigUtils.CelestiteAppConfigFolder, "proxy.conf");
        private static ProxySettings _settings = LoadSettings();
        private static readonly IWebProxy SystemProxy = OperatingSystem.IsWindows()
            ? DynamicWindowsHttpProxy.ProxyInstance : HttpClient.DefaultProxy;
        private static readonly DynamicProxyImpl Instance = new();

        public static IWebProxy GetProxy() => Instance;
        public static string ProxyAddress => Volatile.Read(ref _settings).Proxy?.Address?.AbsoluteUri ?? string.Empty;
        public static bool PassToGames => Volatile.Read(ref _settings).PassToGames;

        public static string GetGameProxyAddress()
        {
            var settings = Volatile.Read(ref _settings);
            return settings.PassToGames ? settings.Proxy?.Address?.AbsoluteUri ?? string.Empty : string.Empty;
        }

        private IWebProxy SelectedProxy => (IWebProxy?)Volatile.Read(ref _settings).Proxy ??
            (ConfigUtils.GetBypassSystemProxy() ? HttpNoProxy.Instance : SystemProxy);

        public ICredentials? Credentials
        {
            get => SelectedProxy.Credentials;
            set => SelectedProxy.Credentials = value;
        }
        public Uri? GetProxy(Uri destination) => SelectedProxy.GetProxy(destination);
        public bool IsBypassed(Uri host) => SelectedProxy.IsBypassed(host);

        public static bool SaveSettings(string address, bool passToGames)
        {
            address = address.Trim();
            WebProxy? proxy = ParseProxy(address);
            if (address.Length > 0 && proxy == null) return false;
            Directory.CreateDirectory(ConfigUtils.CelestiteAppConfigFolder);
            string temporaryFile = SettingsFile + ".tmp";
            File.WriteAllLines(temporaryFile, [proxy?.Address?.AbsoluteUri ?? string.Empty, passToGames.ToString()]);
            File.Move(temporaryFile, SettingsFile, overwrite: true);
            Volatile.Write(ref _settings, new ProxySettings(proxy, passToGames));
            return true;
        }

        private static ProxySettings LoadSettings()
        {
            try
            {
                var lines = File.ReadAllLines(SettingsFile);
                if (lines is [var address, var passToGames])
                    return new ProxySettings(ParseProxy(address), bool.TryParse(passToGames, out bool enabled) && enabled);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new ProxySettings(null, false);
        }

        private static WebProxy? ParseProxy(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Host.Length == 0 ||
                uri.Scheme is not ("http" or "https" or "socks5") || uri.Port is <= 0 or > 65535 ||
                uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                return null;
            return new WebProxy(uri);
        }
    }
}

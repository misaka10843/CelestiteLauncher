using System;
using System.Diagnostics;
using System.IO;
using Celestite.I18N;
using Celestite.Network.DynamicProxy;
using Cysharp.Text;

namespace Celestite.Utils
{
    public enum ProcessFailedReason
    {
        Success,
        PathIsEmpty,
        FileNotExists,
        SystemFailed,
    }

    public static partial class ProcessUtils
    {
        public static ProcessFailedReason CreateProcess(string path, out Process? p, out Exception? systemReason,
            bool isAdministrator = false)
            => CreateProcess(path, string.Empty, out p, out systemReason, isAdministrator);

        public static ProcessFailedReason CreateProcess(string path, string arguments, out Process? p, out Exception? systemReason,
            bool isAdministrator = false)
            => CreateProcess(path, arguments, out p, out systemReason, isAdministrator, string.Empty);

        private static ProcessFailedReason CreateProcess(string path, string arguments, out Process? p, out Exception? systemReason,
            bool isAdministrator, string proxyAddress)
        {
            systemReason = null;
            p = null;
            if (string.IsNullOrEmpty(path)) return ProcessFailedReason.PathIsEmpty;
            if (!File.Exists(path)) return ProcessFailedReason.FileNotExists;

            var process = new ProcessStartInfo
            {
                FileName = path
            };
            if (isAdministrator)
                process.Verb = "runas";
            process.Arguments = arguments;
            process.UseShellExecute = true;
            if (!isAdministrator && proxyAddress.Length > 0)
            {
                process.UseShellExecute = false;
                foreach (string key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
                {
                    process.Environment[key] = proxyAddress;
                    if (!OperatingSystem.IsWindows())
                        process.Environment[key.ToLowerInvariant()] = proxyAddress;
                }
            }

            try
            {
                p = Process.Start(process);
                return ProcessFailedReason.Success;
            }
            catch (Exception e)
            {
                systemReason = e;
                return ProcessFailedReason.SystemFailed;
            }
        }

        public static ProcessFailedReason LaunchProcessWithProductId(string productId, string path, string argument,
            out Process? process, bool isAdministrator = false, bool direct = false)
        {
            var extraCommandLine = ConfigUtils.GetGameSettingsCommandLine(productId);
            using var commandLine = ZString.CreateStringBuilder();
            commandLine.Append(argument);
            if (!string.IsNullOrEmpty(extraCommandLine))
            {
                commandLine.Append(" ");
                commandLine.Append(extraCommandLine);
            }
            return LaunchProcess(path, commandLine.ToString(), out process, isAdministrator, direct, DynamicProxyImpl.GetGameProxyAddress());
        }

        public static ProcessFailedReason LaunchProcess(string path, string argument, out Process? process, bool isAdministrator = false, bool direct = false)
            => LaunchProcess(path, argument, out process, isAdministrator, direct, string.Empty);

        private static ProcessFailedReason LaunchProcess(string path, string argument, out Process? process, bool isAdministrator, bool direct, string proxyAddress)
        {
            if (!OperatingSystem.IsWindows() && !direct)
            {
                var winePath = ConfigUtils.GetWinePath();
                if (string.IsNullOrEmpty(winePath))
                {
                    NotificationHelper.Warn(Localization.NonWindowsLayerIsRequired);
                    process = null;
                    return ProcessFailedReason.Success;
                }

                argument = $"\"{path}\" {argument}";
                path = winePath;
            }
            var result = CreateProcess(path, argument, out process, out var reason, isAdministrator, proxyAddress);
            if (result == ProcessFailedReason.SystemFailed)
                NotificationHelper.Warn(reason!.Message);
            else if (result == ProcessFailedReason.FileNotExists)
                NotificationHelper.Warn(ZString.Format(Localization.LaunchFailedFileNotFound, path));
            else if (result == ProcessFailedReason.PathIsEmpty)
                NotificationHelper.Warn(ZString.Format(Localization.LaunchFailedDirectoryNotFound, Path.GetDirectoryName(path)));
            return result;
        }

        public static void OpenExternalLink(string link)
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true, Verb = "open" });
        }
    }
}

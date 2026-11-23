using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Celestite.I18N;
using Celestite.Network;
using Celestite.Network.Downloader;
using Celestite.Network.Models;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using Polly.Timeout;
using ZeroLog;

namespace Celestite.Utils
{
    public class LaunchHelper
    {
        private static readonly Log Logger = LogManager.GetLogger("LaunchHelper");
        public static bool IsInGuiMode() => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime a && a.MainWindow != null;

        public static async UniTask EmulateLaunch(string productId, DmmTypeApiGame gameType)
        {
            if (gameType.IsCl)
            {
                var clInfo = await DmmGamePlayerApiHelper.LaunchClGame(productId, gameType.GameType, default);
                if (ReportLaunchApiFailure(clInfo)) return;
                NotificationHelper.Success(Localization.EmuLaunchSuccess);
                return;
            }

            NotificationHelper.Warn(Localization.GameNotSupported);
        }

        public static async UniTask LaunchGame(string productId, TApiGameType type, string? packageImageUrl = null, bool checkGameExist = false)
        {
            try
            {
                await LaunchGameCore(productId, type, packageImageUrl, checkGameExist);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Unexpected launch failure for {productId}: {ex.GetType().Name}");
                ReportLaunchException(ex);
            }
        }

        internal static bool ReportLaunchApiFailure(DmmGamePlayerApiResult result)
        {
            if (result.Error is { } error)
                ReportLaunchException(error);
            return result.Failed;
        }

        private static void ReportLaunchException(Exception error) =>
            NotificationHelper.Error(error switch
            {
                TimeoutRejectedException or TimeoutException or OperationCanceledException => Localization.GameLaunchTimedOut,
                HttpRequestException => Localization.GameLaunchNetworkFailed,
                _ => Localization.GameLaunchUnexpectedFailure
            });

        private static async UniTask LaunchGameCore(string productId, TApiGameType type, string? packageImageUrl, bool checkGameExist)
        {
            var apiType = DmmTypeApiGameHelper.GetGameTypeDataFromApiGameType(type);
            if (apiType.IsCl)
            {

                if (KashimaDownloadManager.IsProductDownloading(productId))
                {
                    NotificationHelper.Warn(Localization.GameIsDownloading);
                    return;
                }

                if (checkGameExist)
                {
                    var clientGames = await DmmGamePlayerApiHelper.MyGameList();
                    if (ReportLaunchApiFailure(clientGames)) return;

                    if (!clientGames.Value.Any(x => x.ProductId == productId && x.Type == type))
                    {
                        var advancedType = DmmTypeApiGameHelper.GetGameTypeDataFromApiGameType(type);
                        var targetGameType = await DmmGamePlayerApiHelper.GetGameTypeFromOldKeys(productId, REQUEST_FLOOR.FREE, adult: advancedType.IsAdult);
                        if (ReportLaunchApiFailure(targetGameType)) return;
                        var addGame = await DmmGamePlayerApiHelper.AddStoreProductToMyGame(targetGameType.Value);
                        if (ReportLaunchApiFailure(addGame)) return;
                    }
                }

                ClInfo clInfo;

                var launchAfterUpdate = true;
                var createShortcutAfterUpdate = false;
                var nextOpenUpdateWindow = false;

                ConfigUtils.TryGetContent(productId, type, out var content);
                if (content?.Detail.Installed == true)
                {
                    // TODO: 注意需要解析conversion_url
                    var launchCl = await DmmGamePlayerApiHelper.LaunchClGame(productId, type);
                    if (ReportLaunchApiFailure(launchCl)) return;
                    if (content.Detail.Version == launchCl.Value.LatestVersion)
                    {
                        var result = ProcessUtils.LaunchProcessWithProductId(productId,
                            Path.Combine(content.Detail.Path, launchCl.Value.ExecFileName), launchCl.Value.ExecuteArgs, out var p,
                            launchCl.Value.IsAdministrator);
                        p?.Dispose();
                        if (result == ProcessFailedReason.Success && p != null)
                            NotificationHelper.Info(Localization.GameLaunchStarted);
                        if (result is ProcessFailedReason.Success or ProcessFailedReason.SystemFailed)
                            return;
                    }
                    clInfo = launchCl.Value;
                    nextOpenUpdateWindow = true;
                }
                else
                {
                    var installCl = await DmmGamePlayerApiHelper.GetInstallClInfo(productId, type);
                    if (ReportLaunchApiFailure(installCl)) return;
                    clInfo = installCl.Value;

                    if (!IsInGuiMode())
                    {
                        Console.WriteLine("Download required, cannot process it in console mode.");
                        return;
                    }
                    (launchAfterUpdate, createShortcutAfterUpdate, ContentDialogResult result) = await KashimaDownloadManager.OpenInstallDialog(clInfo);
                    if (result != ContentDialogResult.Primary) return;
                    Logger.Info(clInfo.FileListUrl);
                    if (installCl.Value.HasInstaller)
                    {
                        NotificationHelper.Warn(Localization.DEBUG_InstallerNotImplemented);
                    }
                }

                if (!IsInGuiMode())
                {
                    Console.WriteLine("Download required, cannot process it in console mode.");
                    return;
                }
                packageImageUrl ??= ZString.Format("https://pics.dmm.com/digital/gameplayer/{0}/{0}pt.jpg", productId);

                var installDirectory = content?.Detail.Path;
                if (string.IsNullOrWhiteSpace(installDirectory))
                    installDirectory = ConfigUtils.DmmGamesDefaultInstallFolder;
                KashimaDownloadManager.PushDownloadInfo(clInfo, type, installDirectory, packageImageUrl, nextOpenUpdateWindow,
                    launchAfterUpdate, createShortcutAfterUpdate).Forget();
                return;
            }

            if (apiType.IsChannelBrowser)
            {
                var launch = await DmmGamePlayerApiHelper.LaunchBrowserGame(productId, type);
                if (ReportLaunchApiFailure(launch)) return;
                ProcessUtils.OpenExternalLink(launch.Value.LaunchUrl);
                return;
            }

            if (apiType.IsSocialBrowser)
            {
                if (ConfigUtils.GetDisableIFrameEx() || !string.IsNullOrEmpty(HttpHelper.ActAuth))
                {
                    ProcessUtils.OpenExternalLink(DmmBrowserGameLaunchHelper.GetBrowserGameUrl(productId, apiType));
                    return;
                }
                var context = await DmmBrowserGameLaunchHelper.GetBrowserPageContext(productId, apiType);
                if (context == null)
                {
                    NotificationHelper.Error(Localization.LaunchBrowserGameFailed);
                    return;
                }
                var launchUrl = DmmBrowserGameLaunchHelper.FindAndInvokeLaunchGame(context);
                if (string.IsNullOrEmpty(launchUrl))
                {
                    var artemisLaunchInfo = await DmmBrowserGameLaunchHelper.ArtemisInitGameFrame(productId, apiType);
                    if (artemisLaunchInfo == null || !string.IsNullOrEmpty(artemisLaunchInfo.Code))
                    {
                        NotificationHelper.Warn(Localization.LaunchBrowserGameFailed);
                        return;
                    }
                    launchUrl = ZString.Concat("https:", artemisLaunchInfo.GameFrameUrl);
                }
                ProcessUtils.OpenExternalLink(launchUrl);
                return;
            }

            NotificationHelper.Warn(Localization.GameNotImplemented);
        }

        public static async UniTask CreateShortcut(string productId, TApiGameType type, string title, string imagePictureUrl)
        {
            if (ConfigUtils.IsContentInstalled(productId, type, out var content))
            {
                var shortcut = await DmmGamePlayerApiHelper.GetShortcutInfo(productId, type);
                if (shortcut.Failed) return;

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop))
                {
                    NotificationHelper.Warn(Localization.GetDesktopPathFailed);
                    return;
                }

                if (OperatingSystem.IsWindows())
                {
                    var internetShortcut = ZString.Format("""
                                                          [InternetShortcut]
                                                          URL=celestite://launch/{0}/{1}
                                                          IconFile={2}
                                                          HotKey=0
                                                          IconIndex=0
                                                          """, productId, type.ToString(), Path.Combine(content!.Detail.Path, shortcut.Value.ExecutableFile));
                    var shortcutPath = Path.Combine(desktop, $"{shortcut.Value.ShortcutFileName}.url");
                    await File.WriteAllTextAsync(shortcutPath, internetShortcut);
                    NotificationHelper.Success(Localization.CreateShortcutSuccess);
                }

                if (OperatingSystem.IsLinux())
                {
                    var shortcutData = await HttpHelper.GetByteArrayWithCacheFromWebAsync(imagePictureUrl);
                    if (shortcutData.Failed) return;

                    using var bitmap = shortcutData.Value;
                    var pngName = $"{ConfigUtils.GetFilePath(productId)}.png";
                    bitmap.Save(pngName);

                    var xdgShortcut = ZString.Format("""
                                                     [Desktop Entry]
                                                     Version=1.0
                                                     Type=Application
                                                     Exec=xdg-open celestite://launch/{0}/{1}
                                                     Icon={2}
                                                     NoDisplay=true
                                                     Terminal=false
                                                     Categories=Application
                                                     Name={3}
                                                     """, productId, type.ToString(), pngName, shortcut.Value.ShortcutFileName);
                    var shortcutPath = Path.Combine(desktop, $"{productId}.desktop");
                    await File.WriteAllTextAsync(shortcutPath, xdgShortcut);
                    Process.Start("gio", $"set {shortcutPath} metadata::trusted true");
                    File.SetUnixFileMode(shortcutPath, File.GetUnixFileMode(shortcutPath) | UnixFileMode.UserExecute);
                    NotificationHelper.Success(Localization.CreateShortcutSuccess);
                }
            }
            else
            {
                NotificationHelper.Warn(Localization.GameIsNotInstalled);
            }
        }
    }
}

using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Celestite.Configs;
using Celestite.Dialogs;
using Celestite.I18N;
using Celestite.Network;
using Celestite.Network.Models;
using Celestite.Utils;
using Celestite.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using ZeroLog;

namespace Celestite.ViewModels.DefaultVM
{
    public partial class LoginPageViewModel : ViewModelBase
    {
        [ObservableProperty] private string _infoBarMessage = string.Empty;
        [ObservableProperty] private InfoBarSeverity _severity = InfoBarSeverity.Warning;
        [ObservableProperty] private bool _canUseLoginButton;
        [ObservableProperty] private bool _browserLoginPending;
        [ObservableProperty] private string? _browserLoginCallback;

        [ObservableProperty] private bool _isDmmLoginEnabled = ConfigUtils.IsDgp5Installed && OperatingSystem.IsWindows();

        private readonly Log _logger = LogManager.GetLogger("Login");

        private static ContentDialog? _registerFormDialog = null;

        public static ContentDialog RegisterFormDialog => LazyInitializer.EnsureInitialized(ref _registerFormDialog,
            () => Dispatcher.UIThread.Invoke(() => new ContentDialog()
            {
                Title = Localization.RegisterSubtitle,
                CloseButtonText = Localization.FormClose,
                Content = new RegisterFormDialog()
            }));

        public LoginPageViewModel()
        {
            if (!Design.IsDesignMode)
                UniTask.Run(() => ProcessAutoLoginCommand.ExecuteAsync(null));
        }

        private async UniTask ProcessUser()
        {
            var userInfo = await DmmGamePlayerApiHelper.GetUserInfo();
            if (userInfo.Failed)
            {
                SetLoginErrorStatus(userInfo.ErrorMessage!);
                return;
            }

            uint target = userInfo.Value.IsProfileRegistered
                ? NavigationFactory.MainNavigation
                : NavigationFactory.DmmProfileReg;
            try
            {
                if (!NavigationFactory.OuterInstance.Navigate(target, animate: false))
                    SetLoginErrorStatus("Unable to open the main interface. Check the application log.");
            }
            catch (Exception ex)
            {
                _logger.Error("Unable to open the main interface after login.", ex);
                SetLoginErrorStatus("Unable to open the main interface. Check the application log.");
            }
        }

        [RelayCommand]
        private async Task ProcessAutoLogin()
        {
            if (!ConfigUtils.TryGetLastLogin(out var account) || account?.AutoLogin != true)
            {
                SetDefaultStatus();
                return;
            }

            SetLoginStatus();
            if (!await DmmOpenApiHelper.CheckValidity(account.LoginSecureId, account.LoginSessionId))
            {
                SetLoginErrorStatus(Localization.BrowserLoginSavedSessionExpired);
                return;
            }

            DmmGamePlayerApiHelper.SetUserCookies(account.LoginSecureId, account.LoginSessionId);
            DmmGamePlayerApiHelper.SetAgeCheckDone();
            SetDefaultStatus();
            await ProcessUser();
        }

        [RelayCommand]
        private async Task ShowRegisterButton()
        {
            await RegisterFormDialog.ShowAsync();
        }

        [RelayCommand]
        private async Task ShowBrowserLogin()
        {
            SetLoginStatus();
            var authorization = await DmmGamePlayerApiHelper.GetAuthLoginUrl();
            if (authorization.Failed || !Uri.TryCreate(authorization.Value?.Url, UriKind.Absolute, out var loginUrl) ||
                loginUrl.Scheme != Uri.UriSchemeHttps || loginUrl.Host != "accounts.dmm.com" ||
                !loginUrl.IsDefaultPort || loginUrl.UserInfo.Length != 0)
            {
                _logger.Warn("DMM browser sign-in could not obtain a valid authorization URL.");
                SetLoginErrorStatus(Localization.BrowserLoginFailed);
                return;
            }

            try
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    BrowserLoginPending = true;
                    CommandLineHelper.DmmBrowserCallback += ReceiveBrowserLoginCallback;
                    InfoBarMessage = Localization.BrowserLoginWaiting;
                });
                await Task.Run(() => ProcessUtils.OpenExternalLink(loginUrl.AbsoluteUri, forceExternal: true));
            }
            catch (Exception ex)
            {
                _logger.Warn("DMM browser sign-in could not open the browser.", ex);
                Dispatcher.UIThread.Invoke(() =>
                {
                    EndBrowserLogin();
                    SetLoginErrorStatus(Localization.BrowserLoginFailed);
                });
            }
        }

        private void ReceiveBrowserLoginCallback(Uri uri) => Dispatcher.UIThread.Post(() =>
        {
            if (!BrowserLoginPending) return;
            if (GetBrowserLoginCode(uri) is not { } code)
            {
                _logger.Warn($"DMM browser callback did not contain a recognized authorization code (query present: {uri.Query.Length > 0}).");
                return;
            }
            CompleteBrowserLogin(code).AsUniTask().Forget();
        });

        [RelayCommand]
        private Task SubmitBrowserLogin()
        {
            if (!BrowserLoginPending) return Task.CompletedTask;
            var code = Uri.TryCreate(BrowserLoginCallback?.Trim(), UriKind.Absolute, out var uri) ? GetBrowserLoginCode(uri) : null;
            if (code == null)
            {
                InfoBarMessage = Localization.BrowserLoginInvalidCallback;
                Severity = InfoBarSeverity.Error;
                return Task.CompletedTask;
            }
            return CompleteBrowserLogin(code);
        }

        [RelayCommand]
        private void CancelBrowserLogin()
        {
            EndBrowserLogin();
            SetDefaultStatus();
        }

        private void EndBrowserLogin()
        {
            CommandLineHelper.DmmBrowserCallback -= ReceiveBrowserLoginCallback;
            BrowserLoginPending = false;
            BrowserLoginCallback = null;
        }

        private async Task CompleteBrowserLogin(string code)
        {
            EndBrowserLogin();
            SetLoginStatus();
            var token = await DmmGamePlayerApiHelper.IssueAccessToken(code);
            if (token.Failed || string.IsNullOrWhiteSpace(token.Value?.AccessToken))
            {
                _logger.Warn($"DMM browser sign-in failed at token exchange (result: {token.ErrorCode}).");
                SetLoginErrorStatus(Localization.BrowserLoginFailed);
                return;
            }

            DmmGamePlayerApiHelper.SetUserHeader(token.Value.AccessToken);
            DmmGamePlayerApiHelper.SetAgeCheckDone();
            SetDefaultStatus();
            await ProcessUser();
        }

        private static string? GetBrowserLoginCode(Uri uri)
        {
            if (!uri.IsDefaultPort || uri.UserInfo.Length != 0)
                return null;
            if (uri.Scheme != "dmmgameplayer" &&
                !(uri.Scheme == Uri.UriSchemeHttps && uri.Host == "webdgp-gameplayer.games.dmm.com" && uri.AbsolutePath == "/login/success"))
                return null;

            var code = System.Web.HttpUtility.ParseQueryString(uri.Query).GetValues("code");
            return code is [var value] && !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && !value.Contains(',')
                ? value
                : null;
        }

        [RelayCommand]
        private void ShowDmmLoginButton()
        {
            if (!OperatingSystem.IsWindows())
            {
                SetLoginErrorStatus(Localization.UnsupportedSystemError);
                return;
            }

            SetLoginStatus();

            var localState = ConfigUtils.ParseLocalState();
            if (localState == null)
            {
                SetLoginErrorStatus(Localization.LoadLocalStateError);
                return;
            }

            // TODO: 多系统
            if (localState.OsCrypt == null || !ConvertEncryptedKeyBase64(localState.OsCrypt.EncryptedKey, out var masterKey))
            {
                SetLoginErrorStatus(Localization.LoadLocalStateError);
                return;
            }
            masterKey = ProtectedData.Unprotect(masterKey, null, DataProtectionScope.CurrentUser);
            UniTask.Run(async () =>
            {
                try
                {
                    await using var sqlConnection = ConfigUtils.OpenDgpCookiesConnection();
                    var version = Meta.GetMeta(sqlConnection, "version");
                    var altHash = ElectronCookie.GetCookieByName(sqlConnection, "althash");
                    var hasAltHash = ElectronCookie.GetCookieByName(sqlConnection, "has_althash");
                    if (altHash == null || hasAltHash == null)
                    {
                        SetLoginErrorStatus(Localization.LoggedUserNotFoundError);
                        return;
                    }

                    var newFormat = version != null && version.Value >= 24;

                    var decryptedAltHash = DecryptCookie(altHash.EncryptedValue.AsSpan(), masterKey, newFormat);
                    var decryptedHasAltHash = DecryptCookie(hasAltHash.EncryptedValue.AsSpan(), masterKey, newFormat);

                    var loginResult =
                        await DmmAltHashLoginHelper.LoginFromAltHash(decryptedAltHash, decryptedHasAltHash);
                    if (loginResult.Failed)
                    {
                        SetLoginErrorStatus(loginResult.Error!);
                        return;
                    }

                    altHash.EncryptedValue = EncryptCookie(loginResult.Value.AltHash.Value, masterKey, newFormat, altHash.HostKey);
                    altHash.ExpiresUtc =
                        (((DateTimeOffset)loginResult.Value.AltHash.Expires).ToUnixTimeSeconds() + 11644473600) *
                        1000000;
                    hasAltHash.EncryptedValue = EncryptCookie(loginResult.Value.HasAltHash.Value, masterKey, newFormat, hasAltHash.HostKey);
                    hasAltHash.ExpiresUtc =
                        (((DateTimeOffset)loginResult.Value.HasAltHash.Expires).ToUnixTimeSeconds() + 11644473600) *
                        1000000;
                    ElectronCookie.UpdateCookie(sqlConnection, altHash);
                    ElectronCookie.UpdateCookie(sqlConnection, hasAltHash);

                    DmmGamePlayerApiHelper.SetUserCookies(loginResult.Value.LoginSecureId, loginResult.Value.LoginSessionId);
                    DmmGamePlayerApiHelper.SetAgeCheckDone();

                    ConfigUtils.ClearLastLogin();
                    SetDefaultStatus();
                    await ProcessUser();
                }
                catch (Exception ex)
                {
                    SetLoginErrorStatus(ex.Message);
                    _logger.Error(string.Empty, ex);
                }
            }).Forget();
        }

        private static bool ConvertEncryptedKeyBase64(string base64, out byte[] result)
        {
            var bytes = ArrayPool<byte>.Shared.Rent(CyBase64.GetMaxBase64DecodeLength(base64.Length));
            try
            {
                if (CyBase64.TryFromBase64String(base64, bytes, out var bytesWritten) && bytesWritten >= 5)
                {
                    result = bytes[5..bytesWritten];
                    return true;
                }
                result = [];
                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        private static string DecryptCookie(Span<byte> encryptedData, byte[] masterKey, bool newFormat = false)
        {
            if (encryptedData.Length < 3 + 12 + 16)
                return string.Empty;
            var nonce = encryptedData[3..15];
            var cipherText = encryptedData[15..^16];
            var tag = encryptedData[^16..(encryptedData.Length)];

            Span<byte> resultBytes = stackalloc byte[cipherText.Length];
            using var aesGcm = new AesGcm(masterKey, 16);
            aesGcm.Decrypt(nonce, cipherText, tag, resultBytes);
            if (newFormat)
                resultBytes = resultBytes[32..];
            return Encoding.UTF8.GetString(resultBytes);
        }

        private static byte[] EncryptCookie(string data, byte[] masterKey, bool newFormat = false, string newFormatDomain = "")
        {
            using var stream = new MemoryStream();
            stream.Write("v10"u8);
            var nonce = RandomNumberGenerator.GetBytes(12);
            using var aesGcm = new AesGcm(masterKey, 16);
            Span<byte> tag = stackalloc byte[16];
            var cipherText = Encoding.UTF8.GetBytes(data);
            if (newFormat && !string.IsNullOrEmpty(newFormatDomain))
            {
                var sha256 = SHA256.HashData(Encoding.UTF8.GetBytes(newFormatDomain));
                var cipherText2 = new byte[sha256.Length + cipherText.Length];
                Array.Copy(sha256, cipherText2, sha256.Length);
                Array.Copy(cipherText, 0, cipherText2, sha256.Length, cipherText.Length);
                cipherText = cipherText2;
            }
            var encryptedCipherText = new byte[cipherText.Length];
            aesGcm.Encrypt(nonce, cipherText, encryptedCipherText, tag);
            stream.Write(nonce);
            stream.Write(encryptedCipherText);
            stream.Write(tag);
            return stream.ToArray();
        }

        private void SetLoginStatus()
        {
            InfoBarMessage = Localization.LoginStatusInfoBarMessage;
            Severity = InfoBarSeverity.Informational;
            CanUseLoginButton = false;
        }
        private void SetLoginErrorStatus(string errorMessage)
        {
            InfoBarMessage = errorMessage;
            Severity = InfoBarSeverity.Error;
            CanUseLoginButton = true;
        }

        private void SetLoginErrorStatus(DmmOpenApiErrorBody error)
        {
            var errorMessage = ZString.Format("[{0}] {1}", error.Code, error.Reason);
            InfoBarMessage = errorMessage;
            Severity = InfoBarSeverity.Error;
            CanUseLoginButton = true;
        }
        private void SetDefaultStatus()
        {
            InfoBarMessage = Localization.DGPLoginInfoBarMessage;
            Severity = InfoBarSeverity.Warning;
            CanUseLoginButton = true;
        }
    }
}

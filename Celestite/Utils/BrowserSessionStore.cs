using System;
using System.IO;
using System.Security.Cryptography;
using ZeroLog;

namespace Celestite.Utils;

internal static class BrowserSessionStore
{
    internal sealed record Session(string AccessToken, long ExpiresAt)
    {
        public bool IsExpired => ExpiresAt > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= ExpiresAt;
    }

    private static readonly Log Logger = LogManager.GetLogger("BrowserSession");
    private static string SessionPath => Path.Combine(ConfigUtils.CelestiteAppConfigFolder, "browser-session.dat");

    public static Session? Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SessionPath)) return null;

        try
        {
            using var file = File.OpenRead(SessionPath);
            if (file.Length is <= 0 or > 32768) return null;
            var encrypted = new byte[(int)file.Length];
            file.ReadExactly(encrypted);
            var content = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try
            {
                using var reader = new BinaryReader(new MemoryStream(content, writable: false));
                if (reader.ReadByte() != 1) return null;
                long expiresAt = reader.ReadInt64();
                string token = reader.ReadString();
                return string.IsNullOrWhiteSpace(token) ? null : new Session(token, expiresAt);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        {
            Logger.Warn($"Could not read the saved browser session ({ex.GetType().Name}).");
            return null;
        }
    }

    public static bool Save(string accessToken, int expiresInSeconds)
    {
        if (!OperatingSystem.IsWindows()) return false;

        string temporaryFile = SessionPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload);
            writer.Write((byte)1);
            writer.Write(expiresInSeconds > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds : 0L);
            writer.Write(accessToken);
            var content = payload.ToArray();
            try
            {
                File.WriteAllBytes(temporaryFile, ProtectedData.Protect(content, null, DataProtectionScope.CurrentUser));
                File.Move(temporaryFile, SessionPath, overwrite: true);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
                CryptographicOperations.ZeroMemory(payload.GetBuffer());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Logger.Warn($"Could not save the browser session ({ex.GetType().Name}).");
            return false;
        }
        finally
        {
            Delete(temporaryFile);
        }
    }

    public static bool Clear() => !OperatingSystem.IsWindows() || Delete(SessionPath);

    private static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn($"Could not remove saved browser session data ({ex.GetType().Name}).");
            return false;
        }
    }
}

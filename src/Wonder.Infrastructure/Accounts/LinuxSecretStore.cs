// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Wonder.Application;

namespace Wonder.Infrastructure;

internal interface IAccountKeyStore
{
    byte[]? ReadKey(string id);
    void StoreKey(string id, byte[] key);
}

/// <summary>Uses libsecret's CLI so no platform-native bindings or plaintext key files are needed.</summary>
internal sealed class LinuxSecretStore : IAccountKeyStore
{
    public byte[]? ReadKey(string id)
    {
        var (code, output) = Run(["lookup", "application", "WonderLauncher", "purpose", "account-vault", "vault", id]);
        if (code == 1 && string.IsNullOrWhiteSpace(output)) return null;
        if (code != 0) throw Unavailable();
        try
        {
            var key = Convert.FromBase64String(output.Trim());
            if (key.Length == 32) return key;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException) { }
        throw Unavailable();
    }

    public void StoreKey(string id, byte[] key)
    {
        var (code, _) = Run(["store", "--label=Wonder Launcher account vault", "application", "WonderLauncher", "purpose", "account-vault", "vault", id],
            Convert.ToBase64String(key));
        if (code != 0) throw Unavailable();
    }

    private static (int Code, string Output) Run(string[] arguments, string? secret = null)
    {
        if (!OperatingSystem.IsLinux()) throw Unavailable();
        // The key is supplied through stdin, never through argv, environment variables or diagnostic logs.
        var start = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw Unavailable();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // Drain both streams concurrently; no stderr text (which may come from another process) reaches the UI.
            var output = ReadLimited(process.StandardOutput, timeout.Token);
            var error = ReadLimited(process.StandardError, timeout.Token);
            if (secret is not null) process.StandardInput.WriteLine(secret);
            process.StandardInput.Close();
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).GetAwaiter().GetResult();
            return (process.ExitCode, output.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception kill) when (kill is Win32Exception or InvalidOperationException) { }
            throw Unavailable();
        }
    }

    private static async Task<string> ReadLimited(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(); var buffer = new char[256];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var count && count > 0)
        {
            if (text.Length + count > 8192) throw new IOException("Secret Service response exceeded its limit.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static AccountAuthenticationException Unavailable() => new("Linux 账户保存需要 secret-tool（Debian/Ubuntu：libsecret-tools）和已解锁的桌面 Secret Service 钥匙环（如 GNOME Keyring）。请在普通桌面用户会话中启用后重试；不会将凭据改存为明文。");
}

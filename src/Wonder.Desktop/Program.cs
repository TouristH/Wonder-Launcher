// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace Wonder.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        LauncherInstanceGuard? instance;
        try
        {
            if (!LauncherInstanceGuard.TryAcquire(LauncherDataPaths.RootDirectory, out instance, out var owner))
            {
                if (!TryActivateExistingWindow(owner))
                    ShowStartupMessage("此数据目录已有 Wonder Launcher 正在运行。请切换到已打开的窗口；如果它正在启动或退出，请稍后重试。\n\n为保护工作区、实例和账户，本次启动已停止。", isError: false);
                return 0;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or Win32Exception)
        {
            ShowStartupMessage($"无法取得 Wonder 数据目录的独占写入权限，本次启动已停止。请检查启动器所在目录、磁盘和写入权限后重试。\n\n{error.Message}", isError: true);
            return 1;
        }

        // Acquire before Avalonia creates MainWindow and reads any persistent user data.
        using (instance)
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WonderApplication>().UsePlatformDetect();

    private static bool TryActivateExistingWindow(LauncherInstanceOwner? owner)
    {
        if (!OperatingSystem.IsWindows() || owner is null) return false;
        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != owner.StartTimeUtcTicks) return false;
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return false;
            if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
            return SetForegroundWindow(window);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static void ShowStartupMessage(string message, bool isError)
    {
        if (OperatingSystem.IsWindows())
            MessageBox(IntPtr.Zero, message, "Wonder Launcher", isError ? 0x10u : 0x40u);
        else Console.Error.WriteLine(message);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}

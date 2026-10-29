// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Infrastructure;

public sealed class InstanceFileOperations : IInstanceFileOperations
{
    public void EnsureGameRoot(string path)
    {
        ValidatePath(path);
        if (!Directory.Exists(Path.Combine(path, "versions")) && !File.Exists(Path.Combine(path, "prismlauncher.cfg")) && !File.Exists(Path.Combine(path, "multimc.cfg")) && !Directory.Exists(Path.Combine(path, "instances")))
            throw new IOException("未能确认此路径是游戏根目录，不能移动或回收整个目录。可通过具体实例卡片管理独立实例。");
    }
    public string InspectDirectory(string path)
    {
        ValidatePath(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new DirectoryNotFoundException("目录不存在：" + path);
        return directory.CreationTimeUtc.Ticks.ToString() + ":" + directory.LastWriteTimeUtc.Ticks;
    }

    private static void EnsureNoLinks(string path)
    {
        var directory = new DirectoryInfo(path);
        // Never follow junctions or symbolic links while determining a destructive scope.
        var pending = new Stack<DirectoryInfo>(); pending.Push(directory);
        while (pending.TryPop(out var current))
            foreach (var child in current.EnumerateFileSystemInfos())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录内存在链接，请先处理后重试：" + child.FullName);
                if (child is DirectoryInfo folder) pending.Push(folder);
            }
    }

    public void EnsureNoVersionDependents(GameInstance instance, string directory)
    {
        if (instance.Source is InstanceSource.Prism or InstanceSource.MultiMc || !Directory.Exists(instance.VersionsDirectory)) return;
        foreach (var sibling in Directory.EnumerateDirectories(instance.VersionsDirectory))
        {
            if (string.Equals(Path.GetFullPath(sibling), Path.GetFullPath(directory), WonderPath.Comparison)) continue;
            if ((File.GetAttributes(sibling) & FileAttributes.ReparsePoint) != 0) throw new IOException("版本目录包含链接，无法确认依赖关系。");
            var name = Path.GetFileName(sibling); var metadata = Path.Combine(sibling, name + ".json");
            if (!File.Exists(metadata)) continue;
            if (new FileInfo(metadata).Length > 8 * 1024 * 1024) throw new IOException("版本元数据过大，无法确认依赖关系：" + metadata);
            using var json = JsonDocument.Parse(File.ReadAllBytes(metadata));
            foreach (var property in new[] { "inheritsFrom", "jar" })
                if (json.RootElement.TryGetProperty(property, out var reference) && reference.ValueKind == JsonValueKind.String && string.Equals(reference.GetString(), instance.VersionId, WonderPath.Comparison))
                    throw new InvalidOperationException("此版本仍被其他实例继承或引用，不能删除：" + name);
        }
    }

    public Task MoveDirectoryAsync(string path, string destination, string expectedIdentity) => Task.Run(() =>
    {
        if (InspectDirectory(path) != expectedIdentity) throw new IOException("原目录发生变化，请重新打开设置。");
        EnsureNoLinks(path);
        ValidatePath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("目标已存在，请选择其他目录昵称或路径。");
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("目标父目录不存在，请先创建或选择已有目录。");
        if (!string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(destination), WonderPath.Comparison))
            throw new IOException("当前支持同一磁盘内移动或重命名。跨磁盘请先通过资源管理器移动，再重新导入。");
        Directory.Move(path, destination);
    });

    public Task RecycleDirectoryAsync(string path, string expectedIdentity)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return RecycleUnixAsync(path, expectedIdentity);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("此平台暂未实现系统回收站操作。");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (InspectDirectory(path) != expectedIdentity) throw new IOException("目录发生变化，请重新确认删除。");
                EnsureNoLinks(path);
                if (OperatingSystem.IsWindows()) RecycleWindows(path);
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Wonder.Recycle" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private async Task RecycleUnixAsync(string path, string expectedIdentity)
    {
        await Task.Run(() =>
        {
            if (InspectDirectory(path) != expectedIdentity) throw new IOException("目录发生变化，请重新确认删除。");
            EnsureNoLinks(path);
        }).ConfigureAwait(false);
        // GIO uses the desktop's trash service. Never substitute rm or a permanent-delete fallback.
        var macOS = OperatingSystem.IsMacOS();
        var start = new ProcessStartInfo(macOS ? "/usr/bin/osascript" : "gio") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (macOS)
        {
            // Pass the path as data, never interpolate it into AppleScript or a shell command.
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\n tell application \"Finder\"\n delete (POSIX file (item 1 of argv) as alias)\n end tell\nend run");
        }
        else { start.ArgumentList.Add("trash"); start.ArgumentList.Add("--"); }
        start.ArgumentList.Add(path);
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { process.Start(); }
        catch (Win32Exception error)
        {
            throw new IOException(macOS ? "无法调用 macOS Finder 废纸篓。请检查自动化权限后重试。" : "无法运行 gio，未修改目录。请安装发行版提供的 GLib/GIO 工具后重试。", error);
        }
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            var detail = (await standardError.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0 || Directory.Exists(path))
                throw new IOException("系统未完成回收，未尝试永久删除。请检查回收站与目录权限。" + (detail.Length == 0 ? "" : "\n" + detail[..Math.Min(detail.Length, 1000)]));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
            throw new IOException("系统回收站操作超时，登记已保留。请先在文件管理器和回收站核对目录位置后重试。");
        }
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("必须使用完整目录路径。");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || string.Equals(full, Path.GetPathRoot(full), WonderPath.Comparison)) throw new IOException("不能对磁盘根目录或网络目录执行此操作。");
        foreach (var folder in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
            if (string.Equals(full, Environment.GetFolderPath(folder), WonderPath.Comparison)) throw new IOException("不能修改个人文件或应用数据根目录：" + full);
        foreach (var special in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
        {
            var protectedPath = Environment.GetFolderPath(special);
            if (string.IsNullOrEmpty(protectedPath)) continue;
            if (string.Equals(full, protectedPath, WonderPath.Comparison) || protectedPath.StartsWith(full + Path.DirectorySeparatorChar, WonderPath.Comparison) ||
                special != Environment.SpecialFolder.UserProfile && full.StartsWith(protectedPath + Path.DirectorySeparatorChar, WonderPath.Comparison))
                throw new IOException("不能修改系统或用户主目录：" + full);
        }
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录路径含有链接，拒绝修改：" + current.FullName);
    }

    [SupportedOSPlatform("windows")]
    private static void RecycleWindows(string path)
    {
        IFileOperation? operation = null; IShellItem? item = null;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
            var itemId = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item));
            // Windows 8+ RECYCLEONDELETE + ADDUNDORECORD; no UI, stop on the first error.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x00080000 | 0x20000000 | 0x00100000 | 0x00000614 | 0x00008000));
            var sink = new RecycleSink();
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            if (aborted || !sink.Recycled || Directory.Exists(path)) throw new IOException("Windows 未完成回收。文件不会改为永久删除，请检查回收站容量和目录权限。");
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr window);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, IntPtr name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, IntPtr name);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes, int result, IntPtr created);
        [PreserveSig] int UpdateProgress(uint total, uint complete);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink : IFileOperationProgressSink
    {
        public bool Recycled { get; private set; }
        // TSF_DELETE_RECYCLE_IF_POSSIBLE must be present. Cancel any permanent-delete fallback.
        public int PreDeleteItem(uint flags, IntPtr item) => (flags & 0x80) != 0 ? 0 : unchecked((int)0x80004004);
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created) { Recycled = result >= 0 && created != IntPtr.Zero; return result; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => result;
        public int PreRenameItem(uint flags, IntPtr item, IntPtr name) => 0;
        public int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created) => 0;
        public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
        public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created) => 0;
        public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
        public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created) => 0;
        public int PreNewItem(uint flags, IntPtr destination, IntPtr name) => 0;
        public int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes, int result, IntPtr created) => 0;
        public int UpdateProgress(uint total, uint complete) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}

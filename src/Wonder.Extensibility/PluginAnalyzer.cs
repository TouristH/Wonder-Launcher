// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Wonder.Extensibility;

public sealed record PluginAnalysis(IReadOnlyList<string> Findings, IReadOnlyList<string> Limitations);

/// <summary>Conservative metadata inspection. Never loads or executes plugin assemblies.</summary>
public static class PluginAnalyzer
{
    public const string Version = "1.0.0";
    public static PluginAnalysis Analyze(string directory)
    {
        var findings = new SortedSet<string>(StringComparer.Ordinal);
        var limitations = new SortedSet<string>(StringComparer.Ordinal)
        {
            "结果来自静态 API 引用分析，不能确定调用是否发生、访问的具体数据或运行时动态代码；未检测到不代表没有此能力。"
        };
        var files = EnumerateFiles(directory);
        var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".dll" or ".exe" or ".so" or ".dylib" or ".wasm" or ".js" or ".ps1" or ".bat" or ".cmd" or ".sh" or ".py")) continue;
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (extension is not (".dll" or ".exe"))
            {
                limitations.Add($"包含无法分析的本地库、脚本或其他可执行内容：{relative}");
                continue;
            }
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                {
                    limitations.Add($"无法分析本地二进制中的行为：{relative}");
                    continue;
                }
                var metadata = pe.GetMetadataReader();
                if (metadata.IsAssembly) assemblyNames.Add(metadata.GetString(metadata.GetAssemblyDefinition().Name));
                if (pe.PEHeaders.CorHeader is { } header && (header.Flags & CorFlags.ILOnly) == 0)
                    limitations.Add($"包含本地或混合模式代码，无法完整分析：{relative}");
                foreach (var reference in metadata.AssemblyReferences)
                    references.Add(metadata.GetString(metadata.GetAssemblyReference(reference).Name));
                var categories = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var reference in metadata.MemberReferences)
                {
                    var member = metadata.GetMemberReference(reference);
                    if (member.Parent.Kind != HandleKind.TypeReference) continue;
                    var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                    Classify(metadata.GetString(type.Namespace), metadata.GetString(type.Name),
                        metadata.GetString(member.Name), categories, limitations, relative);
                }
                foreach (var reference in metadata.TypeReferences)
                {
                    var type = metadata.GetTypeReference(reference);
                    Classify(metadata.GetString(type.Namespace), metadata.GetString(type.Name), "", categories, limitations, relative);
                }
                foreach (var handle in metadata.MethodDefinitions)
                {
                    var method = metadata.GetMethodDefinition(handle);
                    if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                    {
                        categories.Add("调用系统或本地库");
                        limitations.Add($"包含 P/Invoke 调用，本地函数行为无法完整判断：{relative}");
                    }
                }
                foreach (var category in categories) findings.Add($"{category}（{relative}）");
            }
            catch (Exception error) when (error is BadImageFormatException or IOException or InvalidOperationException or ArgumentException)
            {
                limitations.Add($"无法读取二进制元数据，不能判断其行为：{relative}");
            }
        }
        var missing = references.Where(name => !assemblyNames.Contains(name) &&
            name is not ("mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" or "Wonder.Extensibility.Contracts") &&
            !name.StartsWith("System.", StringComparison.Ordinal)).ToArray();
        if (missing.Length > 0)
            limitations.Add($"未在安装包中找到依赖，无法分析它们的实现：{string.Join("、", missing.Take(5))}" +
                (missing.Length > 5 ? $" 等 {missing.Length} 项" : ""));
        return new(findings.ToArray(), limitations.ToArray());
    }

    private static void Classify(string ns, string type, string member, SortedSet<string> findings,
        SortedSet<string> limitations, string file)
    {
        if (ns == "System.IO" && type is "File" or "FileInfo" or "Directory" or "DirectoryInfo" or "FileStream" or "FileSystemWatcher" or "DriveInfo" or "StreamReader" or "StreamWriter" or "RandomAccess" ||
            ns.StartsWith("System.IO.IsolatedStorage", StringComparison.Ordinal) ||
            ns.StartsWith("System.IO.MemoryMappedFiles", StringComparison.Ordinal) ||
            ns == "System.IO.Compression" && type is "ZipFile" or "ZipFileExtensions") findings.Add("访问文件或目录（可能读取、写入或删除）");
        if (ns.StartsWith("System.Net", StringComparison.Ordinal)) findings.Add("访问网络（可能发送或接收数据）");
        if (ns == "System.Diagnostics" && type is "Process" or "ProcessStartInfo") findings.Add("访问或启动外部进程");
        if (ns == "Microsoft.Win32" && type.StartsWith("Registry", StringComparison.Ordinal)) findings.Add("访问系统注册表");
        if (type.Contains("Clipboard", StringComparison.Ordinal)) findings.Add("访问剪贴板");
        if (ns == "System" && type == "Environment") findings.Add("访问系统环境、用户信息或进程状态");
        if (ns.StartsWith("System.Security.Cryptography", StringComparison.Ordinal) && type.Contains("Protected", StringComparison.Ordinal)) findings.Add("访问系统数据保护接口");
        if (ns.StartsWith("System.Management", StringComparison.Ordinal) || ns.StartsWith("Windows.Devices", StringComparison.Ordinal)) findings.Add("查询系统或硬件设备");
        if (ns.StartsWith("System.Reflection", StringComparison.Ordinal) || ns == "System.Runtime.Loader" ||
            ns == "System" && type is "Activator" or "AppDomain" or "Type" ||
            ns == "System.Runtime.InteropServices" && type is "NativeLibrary" or "Marshal")
        {
            findings.Add("反射、动态加载或本地互操作");
            limitations.Add($"存在动态调用机制，无法完整判断实际加载的代码与行为：{file}");
        }
    }

    internal static string[] EnumerateFiles(string directory)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(directory));
        long size = 0;
        var count = 0;
        while (pending.TryPop(out var current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("插件目录不能包含链接。");
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                if (++count > 2048) throw new InvalidDataException("插件文件数量超过 2048。");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("插件文件不能包含链接。");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else
                {
                    size += new FileInfo(path).Length;
                    if (size > 128L * 1024 * 1024) throw new InvalidDataException("插件文件超过 128 MiB。");
                    files.Add(path);
                }
            }
        }
        return files.OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal).ToArray();
    }
}

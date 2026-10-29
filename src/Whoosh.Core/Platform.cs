using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Whoosh.Core;

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public const string Family = "MyWhooshTechnologyService.644173E064ED2_eps1123pz0kt0";
    public const string PackageName = "MyWhooshTechnologyService.644173E064ED2";
    public const string GameVersion = "6.2.3.0";
    public const string GameHash = "3BE5DC0D910CE829C5BA5657A70038D0C8427D600181223F8CDB23BF3C0FDBF5";
    public const string PakHash = "0163095F3A0A1FC7403F29186BBB252A2847DF163F3711296C36ACF9B34C4C29";
    public static readonly IReadOnlyDictionary<string, string> PayloadHashes = new Dictionary<string, string>
    {
        ["MyWhooshChinese_P.pak"] = PakHash,
        ["courses.json"] = "2E9885819DAF02CC3A29BBD00A8D7136C490C9C3B8EB976C3C1BC17B9BF57A09",
        ["categories.json"] = "1A6211833306164351DCA52C4DD23688CA6BE1C7F86AB7F4211E798D0578F120"
    };
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyWhooshHdZh");
    public static string Payload => Path.Combine(Home, "payload", "v1.1.0");
    public static string State => Path.Combine(Home, "state", Family);
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
}

public sealed record Target(string Name, string Family, string Version, string GameRoot, string SavedRoot)
{
    public string GamePak => Path.Combine(GameRoot, "MyWhoosh", "Content", "Paks", "pakchunk0-Windows.pak");
}

public static class SafeFiles
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("拒绝绝对路径或设备路径。");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is ".." or "." or "" || p.EndsWith(' ') || p.EndsWith('.'))) throw new InvalidDataException("拒绝不安全相对路径。");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("路径超出允许目录。");
        NoLinks(full);
        return full;
    }
    public static void NoLinks(string path)
    {
        for (string? cur = Path.GetFullPath(path); cur != null; cur = Path.GetDirectoryName(cur))
        {
            if ((File.Exists(cur) || Directory.Exists(cur)) && (File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("拒绝符号链接或目录联接。");
        }
    }
    public static bool AllowedTarget(string rel)
    {
        rel = rel.Replace('\\', '/');
        return rel == "Paks/MyWhooshChinese_P.pak" || rel == "PersistentDownloadDir/DefaultCache/Cycling/Workouts/WorkoutCategory.json" ||
            ((rel.StartsWith("PersistentDownloadDir/DefaultCache/Cycling/Trainingplans/Workouts/", StringComparison.Ordinal) ||
              rel.StartsWith("PersistentDownloadDir/DefaultCache/Cycling/Workouts/Categories/", StringComparison.Ordinal)) && rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }
    public static byte[] ReadBounded(string path, long limit = 16 * 1024 * 1024)
    {
        NoLinks(path);
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length > limit) throw new InvalidDataException("文件超过安全大小上限。");
        byte[] data = new byte[checked((int)f.Length)]; f.ReadExactly(data); return data;
    }
    public static void AtomicWrite(string path, byte[] data)
    {
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".mwzh-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { f.Write(data); f.Flush(true); }
            if (HashFile(temp) != Hash(data)) throw new IOException("临时写入校验失败。");
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class WindowsTarget
{
    public static Target Detect(bool diagnostic = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("真实游戏操作仅支持 Windows x64。");
        string script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); @(Get-AppxPackage -Name 'MyWhooshTechnologyService.*' -ErrorAction Stop | Select-Object Name,PackageFamilyName,Version,InstallLocation) | ConvertTo-Json -Compress";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("无法查询商店安装包。");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("商店包查询超时。"); }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output.Result)) throw new IOException("当前 Windows 账户未找到 MyWhoosh 商店包。");
        using var doc = JsonDocument.Parse(output.Result);
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : [doc.RootElement];
        var exact = rows.Where(r => r.GetProperty("Name").GetString() == AppInfo.PackageName).ToArray();
        if (exact.Length != 1 && !(diagnostic && rows.Length == 1)) throw new InvalidDataException("未找到唯一受支持的商店包；多个候选需要人工区分，不会安装。");
        var p = exact.Length == 1 ? exact[0] : rows[0]; string family = p.GetProperty("PackageFamilyName").GetString()!;
        return new Target(p.GetProperty("Name").GetString()!, family, p.GetProperty("Version").GetString()!, p.GetProperty("InstallLocation").GetString()!,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalCache", "Local", "MyWhoosh", "Saved"));
    }
    public static void AssertSupported(Target target)
    {
        if (target.Name != AppInfo.PackageName || target.Family != AppInfo.Family || target.Version != AppInfo.GameVersion)
            throw new InvalidDataException("构建不在实测白名单；不会修改游戏。");
        SafeFiles.NoLinks(target.GamePak); SafeFiles.NoLinks(target.SavedRoot);
        if (!Directory.Exists(target.SavedRoot)) throw new DirectoryNotFoundException("请用当前账户先启动游戏并进入训练列表。");
        if (SafeFiles.HashFile(target.GamePak) != AppInfo.GameHash) throw new InvalidDataException("主资源哈希不匹配；不能靠修改哈希配置绕过兼容性验证。");
    }
    public static void AssertClosed()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.ProcessName.StartsWith("MyWhoosh", StringComparison.OrdinalIgnoreCase) && !p.ProcessName.Equals("MyWhooshZH", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请完全退出 MyWhoosh 游戏和旧版安装器后重试。");
            }
        }
    }
}

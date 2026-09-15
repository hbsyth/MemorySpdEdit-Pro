namespace SpdEditor;

/// <summary>
/// 用户启动位置（原生启动器 exe 旁）。备份 BIN 等应写入此处，而非 AppData 解压缓存。
/// </summary>
internal static class AppPaths
{
    /// <summary>启动器传入的程序目录；未设置时为空。</summary>
    public static string HomeDirectory { get; private set; } = "";

    public static void Initialize(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            const string prefix = "--app-home=";
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                TrySetHome(a[prefix.Length..]);
                return;
            }

            if (a.Equals("--app-home", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                TrySetHome(args[i + 1]);
                return;
            }
        }

        string? env = Environment.GetEnvironmentVariable("MEMORYSPDEDIT_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            TrySetHome(env);
    }

    private static void TrySetHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (Directory.Exists(full) && !IsPayloadCacheDirectory(full))
                HomeDirectory = full;
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>启动器解压缓存：%LocalAppData%\MemorySpdEdit-Pro</summary>
    public static bool IsPayloadCacheDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            string full = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string cache = Path.Combine(local, "MemorySpdEdit-Pro");
            return full.Equals(cache, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

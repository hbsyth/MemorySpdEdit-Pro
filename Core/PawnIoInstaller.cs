using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace SpdEditor.Core;

/// <summary>
/// 检测 / 安装官方签名版 PawnIO 驱动（打包自 PawnIO_setup.exe）。
/// </summary>
public static class PawnIoInstaller
{
    public const string SetupFileName = "PawnIO_setup.exe";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string EmbeddedResourceName = "SpdEditor.Assets.PawnIO_setup.exe";

    /// <summary>是否已安装 PawnIO（注册表或 Program Files）。</summary>
    public static bool IsInstalled()
    {
        if (TryGetInstalledVersion(out _))
            return true;

        try
        {
            string dll = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PawnIO",
                "PawnIOLib.dll");
            return File.Exists(dll);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>已安装版本号；未安装返回 false。</summary>
    public static bool TryGetInstalledVersion(out Version? version)
    {
        version = null;
        try
        {
            if (TryReadDisplayVersion(RegistryHive.LocalMachine, RegistryView.Registry64, out version))
                return true;
            if (TryReadDisplayVersion(RegistryHive.LocalMachine, RegistryView.Registry32, out version))
                return true;
            // 默认视图再试一次（兼容部分安装路径）
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var v))
            {
                version = v;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    /// <summary>
    /// 若未安装则提示并运行内置安装包；已安装则直接成功。
    /// </summary>
    public static bool EnsureInstalled(IWin32Window? owner, Action<string>? log, out string message)
    {
        if (IsInstalled())
        {
            TryGetInstalledVersion(out var ver);
            message = ver != null ? $"PawnIO 已安装（v{ver}）" : "PawnIO 已安装";
            log?.Invoke($"[OK] {message}");
            return true;
        }

        var answer = MessageBox.Show(
            owner,
            "SMBus 读取需要安装官方签名驱动 PawnIO。\n\n" +
            "检测到本机尚未安装，是否立即安装？\n" +
            "（安装包已内置于本程序，来源：pawnio.eu / namazso）",
            "安装 PawnIO 驱动",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            message = "已取消安装 PawnIO，无法打开 SMBus";
            log?.Invoke($"[WARN] {message}");
            return false;
        }

        if (!SmbusSpdService.IsAdministrator())
        {
            message = "安装 PawnIO 需要管理员权限，请右键以管理员身份运行本程序";
            log?.Invoke($"[ERR] {message}");
            return false;
        }

        log?.Invoke("[INFO] 正在安装 PawnIO 签名驱动...");
        if (!RunBundledSetup(out int exitCode, out string setupError))
        {
            message = setupError;
            log?.Invoke($"[ERR] {message}");
            return false;
        }

        // ERROR_SUCCESS_REBOOT_REQUIRED = 3010
        if (exitCode is 3010)
        {
            message = "PawnIO 已安装，但需要重启计算机后才能使用 SMBus";
            log?.Invoke($"[WARN] {message}");
            MessageBox.Show(owner, message, "需要重启", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (exitCode != 0)
        {
            message = $"PawnIO 安装失败（退出码 {exitCode}）";
            log?.Invoke($"[ERR] {message}");
            return false;
        }

        if (!IsInstalled())
        {
            message = "安装程序已结束，但仍未检测到 PawnIO，请重启后再试或手动运行 Assets\\PawnIO_setup.exe";
            log?.Invoke($"[ERR] {message}");
            return false;
        }

        TryGetInstalledVersion(out var installed);
        message = installed != null ? $"PawnIO 安装成功（v{installed}）" : "PawnIO 安装成功";
        log?.Invoke($"[OK] {message}");
        return true;
    }

    /// <summary>释放并静默运行内置 PawnIO_setup.exe（-install -silent）。</summary>
    public static bool RunBundledSetup(out int exitCode, out string error)
    {
        exitCode = -1;
        error = "";
        string? setupPath = null;

        try
        {
            setupPath = ResolveSetupPath();
            if (setupPath == null || !File.Exists(setupPath))
            {
                error = "未找到内置 PawnIO_setup.exe，请重新发布/安装本程序";
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "-install -silent",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? AppContext.BaseDirectory,
            };
            // 未提权时请求 UAC（正常 SMBus 流程已要求管理员）
            if (!SmbusSpdService.IsAdministrator())
                psi.Verb = "runas";

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "无法启动 PawnIO 安装程序";
                return false;
            }

            proc.WaitForExit();
            exitCode = proc.ExitCode;
            return true;
        }
        catch (Exception ex)
        {
            error = $"启动 PawnIO 安装程序失败: {ex.Message}";
            return false;
        }
        finally
        {
            // 若从临时目录释放，安装结束后可删除；程序目录内的保留
            try
            {
                if (setupPath != null
                    && setupPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                    && File.Exists(setupPath))
                    File.Delete(setupPath);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>优先用程序目录旁文件，否则从嵌入资源释放到临时目录。</summary>
    public static string? ResolveSetupPath()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, SetupFileName);
        if (File.Exists(beside))
            return beside;

        string assets = Path.Combine(AppContext.BaseDirectory, "Assets", SetupFileName);
        if (File.Exists(assets))
            return assets;

        return ExtractEmbeddedSetup();
    }

    private static string? ExtractEmbeddedSetup()
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream(EmbeddedResourceName);
        if (stream == null)
        {
            // 兼容未指定 LogicalName 时的默认资源名
            string? fallback = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("PawnIO_setup.exe", StringComparison.OrdinalIgnoreCase));
            if (fallback == null)
                return null;
            using var s2 = asm.GetManifestResourceStream(fallback);
            if (s2 == null) return null;
            return WriteTempSetup(s2);
        }

        return WriteTempSetup(stream);
    }

    private static string WriteTempSetup(Stream stream)
    {
        string path = Path.Combine(Path.GetTempPath(), $"SpdEditor_{SetupFileName}");
        using var fs = File.Create(path);
        stream.CopyTo(fs);
        return path;
    }

    private static bool TryReadDisplayVersion(RegistryHive hive, RegistryView view, out Version? version)
    {
        version = null;
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var sub = baseKey.OpenSubKey(UninstallKey);
        return Version.TryParse(sub?.GetValue("DisplayVersion") as string, out version);
    }
}

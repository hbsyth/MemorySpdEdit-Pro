using System.Reflection;

namespace SpdEditor;

/// <summary>
/// 产品版本：Ver + 年(2) + . + 周(2) + . + 流水(4)，例 Ver26.38.0001。
/// 以程序集 InformationalVersion 为准（发布打包时写入）。
/// </summary>
public static class AppVersion
{
    public const string ProductTitle = "DDR3/DDR4/DDR5 内存SPD信息修改器 by SuperGun";

    public static string Label { get; } = ResolveLabel();

    public static string WindowTitle => $"{ProductTitle}  {Label}";

    private static string ResolveLabel()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        if (!string.IsNullOrEmpty(info) && info.StartsWith("Ver", StringComparison.OrdinalIgnoreCase))
            return info;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        if (ver is null)
            return "Ver0.00.0000";

        return $"Ver{ver.Major:D2}.{ver.Minor:D2}.{ver.Build:D4}";
    }
}

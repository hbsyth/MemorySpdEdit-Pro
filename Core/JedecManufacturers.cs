namespace SpdEditor.Core;

/// <summary>JEDEC 制造商条目：continuation + ID 码 + 显示名。</summary>
public readonly record struct ManufacturerEntry(byte Continuation, byte Code, string Name)
{
    /// <summary>下拉显示：厂家十六进制 ID + 厂家信息，例如「2C 镁光 (Micron)」。</summary>
    public string DisplayLabel => $"{Code:X2} {Name}";
}

/// <summary>
/// JEDEC JEP106 制造商 ID（SPD 中带奇校验位的 continuation + ID）。
/// 参考：MemTest86+ jedec_id.h / OpenOCD jep106.inc / JEDEC SPD。
/// </summary>
public static class JedecManufacturers
{
    public static readonly ManufacturerEntry UnknownModuleBrand = new(0x00, 0x00, "未知品牌");
    public static readonly ManufacturerEntry UnknownDieBrand = new(0x00, 0x00, "未知颗粒");

    public const int UnknownModuleBrandIndex = 0;
    public const int UnknownDieBrandIndex = 0;

    /// <summary>内存模组品牌（下拉菜单，首项为未知品牌）。</summary>
    public static readonly ManufacturerEntry[] ModuleBrands =
    [
        UnknownModuleBrand,
        M(0, 0x2C, "镁光 (Micron)"),
        M(0, 0x2D, "海力士 (SK Hynix)"),
        M(0, 0x4E, "三星 (Samsung)"),
        M(1, 0x18, "金士顿 (Kingston)"),
        M(2, 0x1E, "海盗船 (Corsair)"),
        M(4, 0x4D, "芝奇 (G.Skill)"),
        M(5, 0x1B, "英睿达 (Crucial)"),
        M(4, 0x4B, "威刚 (A-DATA)"),
        M(4, 0x6F, "十铨 (TeamGroup)"),
        M(5, 0x02, "爱国者 (Patriot)"),
        M(1, 0x7A, "宇瞻 (Apacer)"),
        M(1, 0x4F, "创见 (Transcend)"),
        M(6, 0x53, "广颖 (Silicon Power)"),
        M(8, 0x18, "科赋 (KLEVV)"),
        M(8, 0x13, "光威 (Gloway)"),
        M(8, 0x12, "影驰 (Galaxy)"),
        M(8, 0x71, "阿斯加特 (Asgard)"),
        M(8, 0x75, "玖合 (JUHOR)"),
        M(10, 0x76, "雷克沙 (Lexar)"),
        M(4, 0x43, "记忆科技 (Ramaxel)"),
        M(1, 0x32, "Mushkin"),
        M(1, 0x3A, "PNY"),
        M(1, 0x14, "Smart Modular"),
        M(5, 0x77, "Avant"),
        M(9, 0x6C, "七彩虹 (Colorful)"),
    ];

    /// <summary>DRAM 颗粒厂家（下拉首项为未知颗粒；无法匹配的 ID 一律归为未知颗粒）。</summary>
    public static readonly ManufacturerEntry[] DieManufacturers =
    [
        UnknownDieBrand,
        M(0, 0x2C, "镁光 (Micron)"),
        M(0, 0x2D, "海力士 (SK Hynix)"),
        M(0, 0x4E, "三星 (Samsung)"),
        M(0, 0x41, "Infineon"),
        M(0, 0x01, "AMD"),
        M(0, 0x1A, "Fujitsu"),
        M(0, 0x98, "东芝 (Toshiba)"),
        M(0, 0x89, "Intel"),
        M(1, 0x15, "Hitachi"),
        M(1, 0x4F, "NEC"),
        M(2, 0x7E, "尔必达 (Elpida)"),
        M(2, 0x35, "SpecTek"),
        M(2, 0x1C, "Panasonic"),
        M(3, 0x0B, "南亚 (Nanya)"),
        M(3, 0x83, "华邦 (Winbond)"),
        M(3, 0x94, "晶豪 (ESMT)"),
        M(4, 0x62, "ISSI"),
        M(4, 0xE5, "钰创 (Etron)"),
        M(5, 0x1B, "英睿达 (Crucial)"),
        M(5, 0x57, "力晶 (Powerchip)"),
        M(8, 0x0E, "Zentel"),
        M(10, 0x11, "长鑫 (CXMT)"),
        M(10, 0x2C, "晋华 (JHICC)"),
    ];

    /// <summary>从显示名提取英文品牌（用于 BIN 文件名）。可带或不带前缀十六进制 ID。</summary>
    public static string GetEnglishName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || displayName == UnknownModuleBrand.Name
            || displayName == UnknownDieBrand.Name
            || displayName == UnknownModuleBrand.DisplayLabel
            || displayName == UnknownDieBrand.DisplayLabel
            || displayName == "未知")
            return "Unknown";

        string name = StripHexPrefix(displayName);

        int start = name.IndexOf('(');
        int end = name.IndexOf(')');
        string english = start >= 0 && end > start
            ? name[(start + 1)..end].Trim()
            : name.Trim();

        return english;
    }

    public static string DecodeModuleName(byte continuation, byte code)
    {
        if (IsUnknownId(continuation, code)) return UnknownModuleBrand.Name;
        var match = FindExact(ModuleBrands, continuation, code);
        return match?.Name ?? UnknownModuleBrand.Name;
    }

    public static string DecodeDieName(byte continuation, byte code)
    {
        if (IsUnknownId(continuation, code)) return UnknownDieBrand.Name;
        var match = FindExact(DieManufacturers, continuation, code);
        return match?.Name ?? UnknownDieBrand.Name;
    }

    public static ManufacturerEntry GetModuleByIndex(int index)
    {
        if (index < 0 || index >= ModuleBrands.Length) return UnknownModuleBrand;
        return ModuleBrands[index];
    }

    public static ManufacturerEntry GetDieByIndex(int index)
    {
        if (index < 0 || index >= DieManufacturers.Length) return UnknownDieBrand;
        return DieManufacturers[index];
    }

    public static int FindModuleIndexByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name == UnknownModuleBrand.Name
            || name == UnknownModuleBrand.DisplayLabel
            || name == "未知")
            return UnknownModuleBrandIndex;

        string plain = StripHexPrefix(name);
        for (int i = 1; i < ModuleBrands.Length; i++)
        {
            if (NameMatches(plain, ModuleBrands[i].Name)
                || NameMatches(name, ModuleBrands[i].DisplayLabel))
                return i;
        }
        return UnknownModuleBrandIndex;
    }

    public static int FindDieIndexByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name == UnknownDieBrand.Name
            || name == UnknownDieBrand.DisplayLabel
            || name == "未知")
            return UnknownDieBrandIndex;

        string plain = StripHexPrefix(name);
        for (int i = 1; i < DieManufacturers.Length; i++)
        {
            if (NameMatches(plain, DieManufacturers[i].Name)
                || NameMatches(name, DieManufacturers[i].DisplayLabel))
                return i;
        }
        return UnknownDieBrandIndex;
    }

    private static string StripHexPrefix(string text)
    {
        text = text.Trim();
        // 「2C 镁光 (Micron)」→ 去掉前缀十六进制
        if (text.Length >= 3
            && Uri.IsHexDigit(text[0]) && Uri.IsHexDigit(text[1])
            && (text[2] == ' ' || text[2] == '-'))
            return text[3..].Trim();
        return text;
    }

    private static bool NameMatches(string decoded, string entryName)
    {
        if (decoded == entryName) return true;
        string shortName = entryName.Split('(')[0].Trim();
        return decoded.Contains(shortName, StringComparison.OrdinalIgnoreCase)
               || entryName.Contains(decoded, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknownId(byte continuation, byte code) =>
        (continuation & 0x7F) == 0 && (code & 0x7F) == 0;

    private static ManufacturerEntry? FindExact(ManufacturerEntry[] list, byte continuation, byte code)
    {
        foreach (var e in list)
        {
            if (e.Continuation == continuation && e.Code == code) return e;
            // 兼容无奇校验位的裸 JEP106 ID
            if ((e.Continuation & 0x7F) == (continuation & 0x7F)
                && (e.Code & 0x7F) == (code & 0x7F)
                && e.Continuation != 0)
                return e;
        }
        return null;
    }

    /// <summary>按 JEP106 bank + 7-bit ID 生成 SPD 用奇校验编码。</summary>
    private static ManufacturerEntry M(int bank, byte id, string name) =>
        new(WithOddParity((byte)bank), WithOddParity(id), name);

    private static byte WithOddParity(byte value)
    {
        int v = value & 0x7F;
        int ones = 0;
        for (int t = v; t != 0; t >>= 1)
            ones += t & 1;
        return (byte)(v | ((ones % 2 == 0) ? 0x80 : 0x00));
    }
}

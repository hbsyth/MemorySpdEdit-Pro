namespace SpdEditor.Core;

/// <summary>
/// SPD 可编辑字段写回与校验逻辑（品牌 / 日期 / SN / 型号 / CRC）。
/// </summary>
public static class SpdEditorLogic
{
    /// <summary>允许的最早生产年份（ISO 年）。</summary>
    public const int ProductionDateMinYear = 2018;

    /// <summary>
    /// 写入模组制造商 ID（JEDEC）。
    /// 选「未知品牌」时不改写原字节（读取未识别 ID / 烧录均保持原样）。
    /// </summary>
    public static void ApplyModuleBrand(byte[] data, SpdInfo info, ManufacturerEntry brand)
    {
        if (IsUnknownManufacturer(brand))
            return;

        EnsureSize(data, info.ModuleMfgOffset + 2);
        data[info.ModuleMfgOffset] = brand.Continuation;
        data[info.ModuleMfgOffset + 1] = brand.Code;
    }

    /// <summary>
    /// 写入 DRAM 颗粒制造商 ID（JEDEC）。
    /// 选「未知颗粒」时不改写原字节（读取未识别 ID / 烧录均保持原样）。
    /// </summary>
    public static void ApplyDieBrand(byte[] data, SpdInfo info, ManufacturerEntry brand)
    {
        if (IsUnknownManufacturer(brand))
            return;

        if (info.DieMfgOffset <= 0 && info.MemoryType == SpdMemoryType.Unknown)
            throw new InvalidOperationException("无法写入颗粒厂家：SPD 类型未知或偏移无效");

        EnsureSize(data, info.DieMfgOffset + 2);
        data[info.DieMfgOffset] = brand.Continuation;
        data[info.DieMfgOffset + 1] = brand.Code;
    }

    /// <summary>未知品牌 / 未知颗粒：Continuation/Code 均为 0。</summary>
    private static bool IsUnknownManufacturer(ManufacturerEntry brand) =>
        (brand.Continuation & 0x7F) == 0 && (brand.Code & 0x7F) == 0;

    /// <summary>写入 4 字节序列号（8 位十六进制）。</summary>
    public static void ApplySerialNumber(byte[] data, SpdInfo info, string snHex)
    {
        snHex = new string(snHex.Where(c => Uri.IsHexDigit(c)).ToArray()).PadLeft(8, '0');
        if (snHex.Length > 8) snHex = snHex[^8..];
        EnsureSize(data, info.SnOffset + 4);
        for (int i = 0; i < 4; i++)
            data[info.SnOffset + i] = Convert.ToByte(snHex.Substring(i * 2, 2), 16);
    }

    /// <summary>写入产品型号（ASCII，不足补空格）。</summary>
    public static void ApplyPartNumber(byte[] data, SpdInfo info, string partNumber)
    {
        int size = info.PartNumberSize > 0 ? info.PartNumberSize : 20;
        EnsureSize(data, info.PartOffset + size);
        for (int i = 0; i < size; i++)
            data[info.PartOffset + i] = i < partNumber.Length ? (byte)partNumber[i] : (byte)0x20;
    }

    public static bool IsValidBcdByte(byte value) =>
        (value & 0x0F) <= 9 && ((value >> 4) & 0x0F) <= 9;

    public static byte IntToBcd(int value)
    {
        value = Math.Clamp(value, 0, 99);
        return (byte)(((value / 10) << 4) | (value % 10));
    }

    public static int BcdToInt(byte bcd)
    {
        if (!IsValidBcdByte(bcd)) return -1;
        return ((bcd >> 4) & 0x0F) * 10 + (bcd & 0x0F);
    }

    public static string FormatProductionDate(byte yearBcd, byte weekBcd) =>
        $"{yearBcd:X2}{weekBcd:X2}";

    public static int ProductionDateYearFromBcd(byte yearBcd) => 2000 + BcdToInt(yearBcd);

    public static int GetMaxProductionWeek(int year) =>
        System.Globalization.ISOWeek.GetWeeksInYear(year);

    /// <summary>当前 ISO 年、周（限制生产日期不得超过当前时间）。</summary>
    public static (int Year, int Week) GetCurrentIsoYearWeek()
    {
        var now = DateTime.Now;
        return (System.Globalization.ISOWeek.GetYear(now), System.Globalization.ISOWeek.GetWeekOfYear(now));
    }

    /// <summary>
    /// 解析并校验生产日期 YYWW（BCD）。0000 表示未设置。
    /// </summary>
    public static bool TryParseProductionDate(string input, out byte yearBcd, out byte weekBcd, out string error)
    {
        yearBcd = 0;
        weekBcd = 0;
        error = "";

        string dateHex = new string(input.Where(c => Uri.IsHexDigit(c)).ToArray()).PadLeft(4, '0');
        if (dateHex.Length > 4) dateHex = dateHex[^4..];

        yearBcd = Convert.ToByte(dateHex[..2], 16);
        weekBcd = Convert.ToByte(dateHex[2..], 16);

        if (yearBcd == 0 && weekBcd == 0)
            return true;

        if (!IsValidBcdByte(yearBcd))
        {
            error = "年份必须为 BCD 格式 (每位 0-9，例如 24 表示 2024 年)";
            return false;
        }

        if (!IsValidBcdByte(weekBcd))
        {
            error = "周数必须为 BCD 格式 (每位 0-9，例如 47 表示第 47 周)";
            return false;
        }

        int year = ProductionDateYearFromBcd(yearBcd);
        var (currentYear, currentWeek) = GetCurrentIsoYearWeek();

        if (year < ProductionDateMinYear || year > currentYear)
        {
            error = $"年份必须在 {ProductionDateMinYear % 100:D2}-{currentYear % 100:D2} 之间 (即 {ProductionDateMinYear}-{currentYear} 年)";
            return false;
        }

        int week = BcdToInt(weekBcd);
        int maxWeekInYear = GetMaxProductionWeek(year);
        if (week < 1 || week > maxWeekInYear)
        {
            error = $"{year} 年的周数必须在 01-{maxWeekInYear} 之间";
            return false;
        }

        if (year == currentYear && week > currentWeek)
        {
            error = $"生产日期不能晚于当前时间（当前为 {currentYear} 年第 {currentWeek} 周，输入为第 {week} 周）";
            return false;
        }

        return true;
    }

    public static string RandomProductionDate()
    {
        var rng = Random.Shared;
        var (currentYear, currentWeek) = GetCurrentIsoYearWeek();
        int year = rng.Next(ProductionDateMinYear, currentYear + 1);
        int maxWeek = year == currentYear
            ? Math.Max(1, currentWeek)
            : GetMaxProductionWeek(year);
        int week = rng.Next(1, maxWeek + 1);
        return FormatProductionDate(IntToBcd(year % 100), IntToBcd(week));
    }

    public static bool TryApplyProductionDate(byte[] data, SpdInfo info, string dateHex, out string error)
    {
        if (!TryParseProductionDate(dateHex, out byte yearBcd, out byte weekBcd, out error))
            return false;

        EnsureSize(data, info.DateOffset + 2);
        data[info.DateOffset] = yearBcd;
        data[info.DateOffset + 1] = weekBcd;
        return true;
    }

    /// <summary>按内存类型重算 SPD CRC（DDR4 三段 / DDR5 一段）。</summary>
    public static void UpdateChecksums(byte[] data, SpdMemoryType memoryType)
    {
        switch (memoryType)
        {
            case SpdMemoryType.Ddr4 when data.Length >= 384:
                SpdUtils.SetWord(data, 126, SpdUtils.Crc16Xmodem(data.AsSpan(0, 0x7E)));
                SpdUtils.SetWord(data, 254, SpdUtils.Crc16Xmodem(data.AsSpan(0x80, 0x7E)));
                SpdUtils.SetWord(data, 382, SpdUtils.Crc16Xmodem(data.AsSpan(320, 62)));
                break;
            case SpdMemoryType.Ddr5 when data.Length >= 512:
                SpdUtils.SetWord(data, 510, SpdUtils.Crc16Xmodem(data.AsSpan(0, 0x1FE)));
                break;
        }
    }

    public static string RandomSerial()
    {
        var rng = Random.Shared;
        return $"{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}";
    }

    /// <summary>从品牌显示名提取字母数字前缀（用于随机料号）。</summary>
    public static string ExtractManufacturerPrefix(string brandDisplayName)
    {
        int start = brandDisplayName.IndexOf('(');
        int end = brandDisplayName.IndexOf(')');
        string prefix = start >= 0 && end > start
            ? brandDisplayName[(start + 1)..end].Trim()
            : brandDisplayName.Trim();

        return new string(prefix.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    public static string RandomPartNumber(string manufacturerName, int maxLength = 20)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = Random.Shared;
        maxLength = Math.Clamp(maxLength, 1, 30);

        string prefix = ExtractManufacturerPrefix(manufacturerName);
        if (prefix.Length >= maxLength)
            return prefix[..maxLength];

        int remaining = maxLength - prefix.Length;
        if (remaining == 0)
            return prefix;

        if (prefix.Length == 0)
        {
            return new string(Enumerable.Range(0, maxLength)
                .Select(_ => chars[rng.Next(chars.Length)])
                .ToArray());
        }

        var suffix = new char[remaining];
        for (int i = 0; i < remaining; i++)
            suffix[i] = chars[rng.Next(chars.Length)];
        return prefix + new string(suffix);
    }

    /// <summary>批量模式下序列号按步长递增（无符号环绕）。</summary>
    public static string IncrementSerial(string snHex, int step)
    {
        snHex = new string(snHex.Where(c => Uri.IsHexDigit(c)).ToArray()).PadLeft(8, '0');
        uint value = Convert.ToUInt32(snHex, 16);
        value = unchecked(value + (uint)step);
        return value.ToString("X8");
    }

    /// <summary>对比两份 SPD，返回差异字节列表（地址, 新值）。</summary>
    public static List<(int Addr, byte Val)> GetChanges(byte[] current, byte[] original, int maxSize)
    {
        var changes = new List<(int, byte)>();
        int len = Math.Min(Math.Max(current.Length, original.Length), maxSize);
        for (int i = 0; i < len; i++)
        {
            byte cur = i < current.Length ? current[i] : (byte)0;
            byte prev = i < original.Length ? original[i] : (byte)0;
            if (cur != prev) changes.Add((i, cur));
        }
        return changes;
    }

    private static void EnsureSize(byte[] data, int required)
    {
        if (data.Length < required)
            throw new InvalidOperationException($"SPD 数据长度不足，需要至少 {required} 字节");
    }
}

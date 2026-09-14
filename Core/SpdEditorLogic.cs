using SpdEditor.Core.Xmp;

namespace SpdEditor.Core;

public static class SpdEditorLogic
{
    public static void ApplyModuleBrand(byte[] data, SpdInfo info, ManufacturerEntry brand)
    {
        EnsureSize(data, info.ModuleMfgOffset + 2);
        data[info.ModuleMfgOffset] = brand.Continuation;
        data[info.ModuleMfgOffset + 1] = brand.Code;
    }

    public static void ApplyDieBrand(byte[] data, SpdInfo info, ManufacturerEntry brand)
    {
        EnsureSize(data, info.DieMfgOffset + 2);
        data[info.DieMfgOffset] = brand.Continuation;
        data[info.DieMfgOffset + 1] = brand.Code;
    }

    public static void ApplySerialNumber(byte[] data, SpdInfo info, string snHex)
    {
        snHex = new string(snHex.Where(c => Uri.IsHexDigit(c)).ToArray()).PadLeft(8, '0');
        if (snHex.Length > 8) snHex = snHex[^8..];
        EnsureSize(data, info.SnOffset + 4);
        for (int i = 0; i < 4; i++)
            data[info.SnOffset + i] = Convert.ToByte(snHex.Substring(i * 2, 2), 16);
    }

    public static void ApplyPartNumber(byte[] data, SpdInfo info, string partNumber)
    {
        int size = info.PartNumberSize > 0 ? info.PartNumberSize : 20;
        EnsureSize(data, info.PartOffset + size);
        for (int i = 0; i < size; i++)
        {
            data[info.PartOffset + i] = i < partNumber.Length ? (byte)partNumber[i] : (byte)0x20;
        }
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

    public const int ProductionDateMinYear = 2018;

    public static string FormatProductionDate(byte yearBcd, byte weekBcd) =>
        $"{yearBcd:X2}{weekBcd:X2}";

    public static int ProductionDateYearFromBcd(byte yearBcd) => 2000 + BcdToInt(yearBcd);

    public static int GetMaxProductionWeek(int year) =>
        System.Globalization.ISOWeek.GetWeeksInYear(year);

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
        int currentYear = DateTime.Now.Year;
        if (year < ProductionDateMinYear || year > currentYear)
        {
            error = $"年份必须在 {ProductionDateMinYear % 100:D2}-{currentYear % 100:D2} 之间 (即 {ProductionDateMinYear}-{currentYear} 年)";
            return false;
        }

        int week = BcdToInt(weekBcd);
        int maxWeek = GetMaxProductionWeek(year);
        if (week < 1 || week > maxWeek)
        {
            error = $"{year} 年的周数必须在 01-{maxWeek} 之间";
            return false;
        }

        return true;
    }

    public static string RandomProductionDate()
    {
        var rng = Random.Shared;
        int currentYear = DateTime.Now.Year;
        int year = rng.Next(ProductionDateMinYear, currentYear + 1);
        int maxWeek = GetMaxProductionWeek(year);
        int week = rng.Next(1, maxWeek + 1);
        return FormatProductionDate(IntToBcd(year % 100), IntToBcd(week));
    }

    public static string CurrentProductionDate()
    {
        var now = DateTime.Now;
        int week = Math.Clamp(System.Globalization.ISOWeek.GetWeekOfYear(now), 1, GetMaxProductionWeek(now.Year));
        return FormatProductionDate(IntToBcd(now.Year % 100), IntToBcd(week));
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

    public static void ApplyProductionDate(byte[] data, SpdInfo info, string dateHex)
    {
        if (!TryApplyProductionDate(data, info, dateHex, out string error))
            throw new ArgumentException(error, nameof(dateHex));
    }

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

    public static void ClearSerialNumber(byte[] data, SpdInfo info) =>
        ApplySerialNumber(data, info, "00000000");

    public static string RandomSerial()
    {
        var rng = Random.Shared;
        return $"{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}";
    }

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

    public static string IncrementSerial(string snHex, int step)
    {
        snHex = new string(snHex.Where(c => Uri.IsHexDigit(c)).ToArray()).PadLeft(8, '0');
        uint value = Convert.ToUInt32(snHex, 16);
        value = unchecked(value + (uint)step);
        return value.ToString("X8");
    }

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

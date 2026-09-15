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

    /// <summary>规范化序列号为 8 位大写十六进制（不足补 0，超出取末 8 位）。</summary>
    public static string NormalizeSerial(string? snHex)
    {
        string s = new string((snHex ?? "").Where(c => Uri.IsHexDigit(c)).ToArray()).ToUpperInvariant();
        if (s.Length > 8) s = s[^8..];
        return s.PadLeft(8, '0');
    }

    /// <summary>写入 4 字节序列号（8 位十六进制）。</summary>
    public static void ApplySerialNumber(byte[] data, SpdInfo info, string snHex)
    {
        snHex = NormalizeSerial(snHex);
        EnsureSize(data, info.SnOffset + 4);
        for (int i = 0; i < 4; i++)
            data[info.SnOffset + i] = Convert.ToByte(snHex.Substring(i * 2, 2), 16);
    }

    /// <summary>SPD 产品型号字段最大长度（DDR5）；界面不另限 12 位。</summary>
    public const int PartNumberSpdMax = 30;

    /// <summary>写入产品型号；超出 SPD 规定长度则截断，不足补空格。</summary>
    public static void ApplyPartNumber(byte[] data, SpdInfo info, string partNumber)
    {
        int size = info.PartNumberSize > 0 ? info.PartNumberSize : 20;
        EnsureSize(data, info.PartOffset + size);
        string visible = NormalizeVisiblePartNumber(partNumber, size);
        for (int i = 0; i < size; i++)
            data[info.PartOffset + i] = i < visible.Length ? (byte)visible[i] : (byte)0x20;
    }

    /// <summary>规范化界面型号：可打印 ASCII、去尾部空格；可选截断到 maxLen（SPD 字段长度）。</summary>
    public static string NormalizeVisiblePartNumber(string? partNumber, int maxLen = PartNumberSpdMax)
    {
        string raw = partNumber ?? "";
        var chars = new char[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            chars[i] = c is >= (char)0x20 and <= (char)0x7E ? c : ' ';
        }

        string s = new string(chars).TrimEnd();
        if (maxLen > 0 && s.Length > maxLen)
            s = s[..maxLen];
        return s;
    }

    /// <summary>按内存类型返回 D3 / D4 / D5。</summary>
    public static string MemoryTypeTag(SpdMemoryType memoryType) => memoryType switch
    {
        SpdMemoryType.Ddr5 => "D5",
        SpdMemoryType.Ddr4 => "D4",
        SpdMemoryType.Ddr3 => "D3",
        _ => "D3",
    };


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
        var (currentYear, currentWeek) = GetCurrentIsoYearWeek();
        int maxWeek = Math.Max(1, currentWeek);
        int week = Random.Shared.Next(1, maxWeek + 1);
        return FormatProductionDate(IntToBcd(currentYear % 100), IntToBcd(week));
    }

    /// <summary>
    /// 将手动输入的生产日期自动完善为合法 YYWW（BCD）：补齐、钳制年份/周数，必要时回落到当前周。
    /// </summary>
    public static string AutoCompleteProductionDate(string? input, out bool changed)
    {
        string original = (input ?? "").Trim();
        if (TryParseProductionDate(original, out byte yOk, out byte wOk, out _))
        {
            string ok = FormatProductionDate(yOk, wOk);
            changed = !string.Equals(original, ok, StringComparison.OrdinalIgnoreCase);
            return ok;
        }

        string digits = new string((original).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            changed = original.Length > 0;
            return "0000";
        }

        digits = digits.PadLeft(4, '0');
        if (digits.Length > 4) digits = digits[^4..];

        int yy = int.Parse(digits[..2]);
        int ww = int.Parse(digits[2..]);
        if (yy == 0 && ww == 0)
        {
            changed = !string.Equals(original, "0000", StringComparison.OrdinalIgnoreCase);
            return "0000";
        }

        var (currentYear, currentWeek) = GetCurrentIsoYearWeek();
        int year = 2000 + yy;
        if (year < ProductionDateMinYear) year = ProductionDateMinYear;
        if (year > currentYear) year = currentYear;

        int maxWeek = year == currentYear
            ? Math.Max(1, currentWeek)
            : GetMaxProductionWeek(year);
        if (ww < 1) ww = 1;
        if (ww > maxWeek) ww = maxWeek;

        string fixedDate = FormatProductionDate(IntToBcd(year % 100), IntToBcd(ww));
        changed = !string.Equals(original, fixedDate, StringComparison.OrdinalIgnoreCase);
        return fixedDate;
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

    /// <summary>
    /// 根据 SPD 字节/长度解析内存类型（JEDEC Byte2：0x0B=DDR3，0x0C=DDR4，0x12=DDR5）。
    /// </summary>
    public static SpdMemoryType ResolveMemoryType(byte[] data, SpdMemoryType hinted = SpdMemoryType.Unknown)
    {
        if (hinted is SpdMemoryType.Ddr3 or SpdMemoryType.Ddr4 or SpdMemoryType.Ddr5)
            return hinted;

        return SpdParser.ResolveType(data);
    }

    /// <summary>JEDEC 标准 SPD 映像长度：DDR3=256，DDR4=512，DDR5=1024。</summary>
    public static int GetStandardLength(SpdMemoryType memoryType) => memoryType switch
    {
        SpdMemoryType.Ddr5 => 1024,
        SpdMemoryType.Ddr4 => 512,
        SpdMemoryType.Ddr3 => 256,
        _ => 0,
    };

    /// <summary>不足标准长度时以 0x00 补齐（刷写前自动完善）。</summary>
    public static byte[] EnsureStandardLength(byte[] data, SpdMemoryType memoryType, out bool padded)
    {
        int need = GetStandardLength(memoryType);
        if (need <= 0 || data.Length >= need)
        {
            padded = false;
            return data;
        }

        var resized = new byte[need];
        Buffer.BlockCopy(data, 0, resized, 0, data.Length);
        padded = true;
        return resized;
    }

    /// <summary>将料号区非法字节规范为空格（0x20–0x7E）。</summary>
    public static bool SanitizePartNumberRegion(byte[] data, SpdInfo info)
    {
        if (info.PartOffset < 0 || info.PartNumberSize <= 0) return false;
        if (data.Length < info.PartOffset + info.PartNumberSize) return false;

        bool changed = false;
        for (int i = 0; i < info.PartNumberSize; i++)
        {
            byte b = data[info.PartOffset + i];
            if (b is < 0x20 or > 0x7E)
            {
                data[info.PartOffset + i] = 0x20;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// DDR3 JEDEC Byte0.bit7：0 = CRC 覆盖 0–125（含模组 ID 117–125）；1 = 仅 0–116。
    /// 编辑模组信息区时须清零 bit7，使 CRC 覆盖 SN/日期/厂商 ID。
    /// </summary>
    public static bool EnsureDdr3CrcCoverageBit(byte[] data, SpdMemoryType memoryType)
    {
        if (memoryType != SpdMemoryType.Ddr3 || data.Length < 128)
            return false;
        if ((data[0] & 0x80) == 0)
            return false;
        data[0] = (byte)(data[0] & 0x7F);
        return true;
    }

    /// <summary>
    /// DDR3 CRC 覆盖长度（JEDEC Annex K）：bit7=0 → 126 字节；bit7=1 → 117 字节。
    /// </summary>
    public static int GetDdr3CrcLength(byte[] data) =>
        data.Length > 0 && (data[0] & 0x80) != 0 ? 117 : 126;

    // DDR5 Intel XMP 3.0：Header@640 + Profiles@704/768/832/896/960（各 64B，末 2 字节 CRC）。
    private const int Ddr5XmpMagicOffset = 640;
    private const int Ddr5XmpBlockLen = 64;
    private static readonly int[] Ddr5XmpHeaderAndProfiles =
        [640, 704, 768, 832, 896, 960];

    // DDR5 AMD EXPO：832 起共 128 字节，末 2 字节为块 CRC（与 XMP P3/User1 重叠）。
    private const int Ddr5ExpoMagicOffset = 832;
    private const int Ddr5ExpoBlockLen = 128;

    /// <summary>
    /// 按 JEDEC 规范重算 SPD CRC-16（XMODEM / poly 0x1021，小端存放）。
    /// DDR4：字节 0–125→126/127；128–253→254/255；320–381→382/383。
    /// DDR5：字节 0–509→510/511；若存在 XMP 3.0 / EXPO 则同步重算其块 CRC。
    /// DDR3：Byte0.bit7=0 时 0–125→126/127；bit7=1 时 0–116→126/127。
    /// </summary>
    public static void UpdateChecksums(byte[] data, SpdMemoryType memoryType)
    {
        memoryType = ResolveMemoryType(data, memoryType);
        switch (memoryType)
        {
            case SpdMemoryType.Ddr5 when data.Length >= 512:
                SpdUtils.SetWord(data, 510, SpdUtils.Crc16Xmodem(data.AsSpan(0, 510)));
                UpdateDdr5VendorProfileChecksums(data);
                break;
            case SpdMemoryType.Ddr4 when data.Length >= 384:
                SpdUtils.SetWord(data, 126, SpdUtils.Crc16Xmodem(data.AsSpan(0, 126)));
                SpdUtils.SetWord(data, 254, SpdUtils.Crc16Xmodem(data.AsSpan(128, 126)));
                SpdUtils.SetWord(data, 382, SpdUtils.Crc16Xmodem(data.AsSpan(320, 62)));
                break;
            case SpdMemoryType.Ddr3 when data.Length >= 128:
            {
                int crcLen = GetDdr3CrcLength(data);
                SpdUtils.SetWord(data, 126, SpdUtils.Crc16Xmodem(data.AsSpan(0, crcLen)));
                break;
            }
            default:
                if (data.Length >= 128)
                    SpdUtils.SetWord(data, 126, SpdUtils.Crc16Xmodem(data.AsSpan(0, 126)));
                break;
        }
    }

    /// <summary>
    /// 确保 BIN 符合 JEDEC / 厂商配置区 CRC：若当前校验失败则按规范重算并写回校验字（自动纠错）。
    /// <paramref name="repaired"/> 为 true 表示写入前 CRC 异常、已自动修正。
    /// </summary>
    public static bool EnsureChecksums(byte[] data, SpdMemoryType memoryType, out bool repaired, out string report)
    {
        memoryType = ResolveMemoryType(data, memoryType);
        bool beforeOk = VerifyChecksums(data, memoryType, out _);
        UpdateChecksums(data, memoryType);
        bool afterOk = VerifyChecksums(data, memoryType, out report);
        repaired = !beforeOk && afterOk;
        return afterOk;
    }

    /// <summary>校验当前 BIN 是否符合 JEDEC / 厂商配置区 CRC；返回是否全部通过及明细。</summary>
    public static bool VerifyChecksums(byte[] data, SpdMemoryType memoryType, out string report)
    {
        memoryType = ResolveMemoryType(data, memoryType);
        var parts = new List<string>();
        bool allOk = true;

        void Check(string name, int crcOffset, int start, int length)
        {
            if (data.Length < crcOffset + 2 || data.Length < start + length)
            {
                parts.Add($"{name}: 数据长度不足");
                allOk = false;
                return;
            }

            ushort expect = SpdUtils.Crc16Xmodem(data.AsSpan(start, length));
            ushort actual = (ushort)SpdUtils.GetWord(data, crcOffset);
            if (expect == actual)
            {
                parts.Add($"{name}: OK ({actual:X4})");
            }
            else
            {
                parts.Add($"{name}: FAIL 期望 {expect:X4} 实际 {actual:X4}");
                allOk = false;
            }
        }

        switch (memoryType)
        {
            case SpdMemoryType.Ddr5:
                Check("DDR5 CRC(0-509)", 510, 0, 510);
                if (!VerifyDdr5VendorProfileChecksums(data, parts))
                    allOk = false;
                break;
            case SpdMemoryType.Ddr4:
                Check("DDR4 CRC#0(0-125)", 126, 0, 126);
                Check("DDR4 CRC#1(128-253)", 254, 128, 126);
                Check("DDR4 CRC#2(320-381)", 382, 320, 62);
                break;
            case SpdMemoryType.Ddr3:
            {
                int crcLen = GetDdr3CrcLength(data);
                Check($"DDR3 CRC(0-{crcLen - 1})", 126, 0, crcLen);
                break;
            }
            default:
                Check("Legacy CRC(0-125)", 126, 0, 126);
                break;
        }

        report = string.Join("；", parts);
        return allOk;
    }

    /// <summary>
    /// 重算一块「末 2 字节存放 CRC」的配置区：CRC-16/XMODEM 覆盖前 blockLen-2 字节。
    /// </summary>
    private static bool WriteSectionCrc(byte[] data, int start, int blockLen)
    {
        if (data.Length < start + blockLen)
            return false;

        ushort crc = SpdUtils.Crc16Xmodem(data.AsSpan(start, blockLen - 2));
        int crcOffset = start + blockLen - 2;
        bool changed = data[crcOffset] != (byte)(crc & 0xFF)
            || data[crcOffset + 1] != (byte)((crc >> 8) & 0xFF);
        SpdUtils.SetWord(data, crcOffset, crc);
        return changed;
    }

    private static bool CheckSectionCrc(byte[] data, int start, int blockLen, string name, List<string> parts)
    {
        if (data.Length < start + blockLen)
        {
            parts.Add($"{name}: 数据长度不足");
            return false;
        }

        ushort expect = SpdUtils.Crc16Xmodem(data.AsSpan(start, blockLen - 2));
        ushort actual = (ushort)SpdUtils.GetWord(data, start + blockLen - 2);
        if (expect == actual)
        {
            parts.Add($"{name}: OK ({actual:X4})");
            return true;
        }

        parts.Add($"{name}: FAIL 期望 {expect:X4} 实际 {actual:X4}");
        return false;
    }

    private static bool HasDdr5XmpMagic(byte[] data) =>
        data.Length >= Ddr5XmpMagicOffset + 2
        && data[Ddr5XmpMagicOffset] == 0x0C
        && data[Ddr5XmpMagicOffset + 1] == 0x4A;

    private static bool HasDdr5ExpoMagic(byte[] data) =>
        data.Length >= Ddr5ExpoMagicOffset + 4
        && data[Ddr5ExpoMagicOffset] == (byte)'E'
        && data[Ddr5ExpoMagicOffset + 1] == (byte)'X'
        && data[Ddr5ExpoMagicOffset + 2] == (byte)'P'
        && data[Ddr5ExpoMagicOffset + 3] == (byte)'O';

    /// <summary>DDR5：存在 XMP 3.0 / EXPO 魔数时重算对应块 CRC。</summary>
    private static void UpdateDdr5VendorProfileChecksums(byte[] data)
    {
        if (HasDdr5XmpMagic(data))
        {
            bool expo = HasDdr5ExpoMagic(data);
            foreach (int offset in Ddr5XmpHeaderAndProfiles)
            {
                // EXPO 占用 832–959，与 XMP P3@832 / User1@896 重叠
                if (expo && offset is 832 or 896)
                    continue;
                // Header 始终更新；Profile 仅在有有效载荷时更新（避免改写空槽）
                if (offset != Ddr5XmpMagicOffset && !IsSectionPayloadNonEmpty(data, offset, Ddr5XmpBlockLen))
                    continue;
                WriteSectionCrc(data, offset, Ddr5XmpBlockLen);
            }
        }

        if (HasDdr5ExpoMagic(data))
            WriteSectionCrc(data, Ddr5ExpoMagicOffset, Ddr5ExpoBlockLen);
    }

    /// <summary>DDR5：校验已存在的 XMP 3.0 / EXPO 块 CRC；无魔数则跳过。</summary>
    private static bool VerifyDdr5VendorProfileChecksums(byte[] data, List<string> parts)
    {
        bool allOk = true;
        bool any = false;

        if (HasDdr5XmpMagic(data))
        {
            any = true;
            bool expo = HasDdr5ExpoMagic(data);
            string[] names =
            [
                "XMP头", "XMP配置1", "XMP配置2", "XMP配置3", "XMP用户1", "XMP用户2"
            ];
            for (int i = 0; i < Ddr5XmpHeaderAndProfiles.Length; i++)
            {
                int offset = Ddr5XmpHeaderAndProfiles[i];
                if (expo && offset is 832 or 896)
                    continue;
                if (offset != Ddr5XmpMagicOffset && !IsSectionPayloadNonEmpty(data, offset, Ddr5XmpBlockLen))
                    continue;
                if (!CheckSectionCrc(data, offset, Ddr5XmpBlockLen, names[i], parts))
                    allOk = false;
            }
        }

        if (HasDdr5ExpoMagic(data))
        {
            any = true;
            if (!CheckSectionCrc(data, Ddr5ExpoMagicOffset, Ddr5ExpoBlockLen, "EXPO", parts))
                allOk = false;
        }

        if (!any)
            parts.Add("XMP/EXPO: 未检测到（跳过）");

        return allOk;
    }

    /// <summary>块载荷（不含末尾 CRC）是否含非零字节。</summary>
    private static bool IsSectionPayloadNonEmpty(byte[] data, int start, int blockLen)
    {
        int payload = blockLen - 2;
        if (data.Length < start + payload) return false;
        for (int i = 0; i < payload; i++)
        {
            if (data[start + i] != 0)
                return true;
        }
        return false;
    }

    /// <summary>刷写/保存前完善结果。</summary>
    public sealed class SpdComplianceReport
    {
        public bool Ok { get; init; }
        public SpdMemoryType MemoryType { get; init; }
        public bool LengthPadded { get; init; }
        public bool PartNumberSanitized { get; init; }
        public bool Ddr3CrcCoverageFixed { get; init; }
        public bool CrcRepaired { get; init; }
        public string Summary { get; init; } = "";
    }

    /// <summary>
    /// 刷写/保存前统一完善：标准长度补齐、料号 ASCII 净化、DDR3 CRC 覆盖位、JEDEC/XMP/EXPO CRC、写前校验。
    /// </summary>
    public static bool FinalizeForFlash(ref byte[] data, SpdMemoryType hinted, out SpdComplianceReport report)
    {
        var type = ResolveMemoryType(data, hinted);
        data = EnsureStandardLength(data, type, out bool lengthPadded);

        var info = SpdParser.Parse(data);
        bool pnSanitized = SanitizePartNumberRegion(data, info);
        bool ddr3Fixed = EnsureDdr3CrcCoverageBit(data, type);
        bool crcOk = EnsureChecksums(data, type, out bool crcRepaired, out string crcReport);
        bool validateOk = SpdJedecCompliance.ValidateBeforeWrite(data, type, out string validateReport);

        var notes = new List<string>();
        if (lengthPadded) notes.Add($"已补齐至 {GetStandardLength(type)} 字节");
        if (pnSanitized) notes.Add("料号区非法字节已规范为空格");
        if (ddr3Fixed) notes.Add("已清 DDR3 Byte0.bit7（CRC 覆盖模组信息区 0–125）");
        if (crcRepaired) notes.Add("CRC 已自动纠错");
        notes.Add(crcReport);
        if (!string.IsNullOrEmpty(validateReport))
            notes.Add(validateReport);

        report = new SpdComplianceReport
        {
            Ok = crcOk && validateOk,
            MemoryType = type,
            LengthPadded = lengthPadded,
            PartNumberSanitized = pnSanitized,
            Ddr3CrcCoverageFixed = ddr3Fixed,
            CrcRepaired = crcRepaired,
            Summary = string.Join("；", notes),
        };
        return report.Ok;
    }

    public static string RandomSerial()
    {
        var rng = Random.Shared;
        return $"{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}{rng.Next(0, 256):X2}";
    }

    /// <summary>
    /// 随机产品型号：厂家英文(首空格前、首字母大写其余小写) + D3/D4/D5 + 容量两位(补0) + 4位随机大写字母数字。
    /// 写入 SPD 时由 <see cref="ApplyPartNumber"/> 按类型规定长度截断并补空格。
    /// </summary>
    public static string RandomPartNumber(string manufacturerName, SpdMemoryType memoryType, int capacityGb)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = Random.Shared;

        string typeTag = MemoryTypeTag(memoryType);
        string capacity = Math.Clamp(capacityGb, 0, 99).ToString("D2");
        string english = JedecManufacturers.GetEnglishName(manufacturerName);
        string brand = FormatManufacturerBrand(english);

        var suffix = new char[4];
        for (int i = 0; i < 4; i++)
            suffix[i] = chars[rng.Next(chars.Length)];

        return brand + typeTag + capacity + new string(suffix);
    }

    /// <summary>厂家英文用于型号：取首空格前片段，仅保留字母数字，首字母大写、其余小写。</summary>
    public static string FormatManufacturerBrand(string englishName)
    {
        string name = (englishName ?? "").Trim();
        int space = name.IndexOf(' ');
        if (space >= 0)
            name = name[..space];

        string letters = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (letters.Length == 0 || letters.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return "Oem";

        if (letters.Length == 1)
            return letters.ToUpperInvariant();

        return char.ToUpperInvariant(letters[0]) + letters[1..].ToLowerInvariant();
    }

    /// <summary>从品牌显示名提取字母数字前缀（用于其它场景）。</summary>
    public static string ExtractManufacturerPrefix(string brandDisplayName)
    {
        int start = brandDisplayName.IndexOf('(');
        int end = brandDisplayName.IndexOf(')');
        string prefix = start >= 0 && end > start
            ? brandDisplayName[(start + 1)..end].Trim()
            : brandDisplayName.Trim();

        return new string(prefix.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
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

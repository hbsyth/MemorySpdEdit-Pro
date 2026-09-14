namespace SpdEditor.Core;

public enum SpdMemoryType
{
    Unknown,
    Ddr4,
    Ddr5,
}

/// <summary>解析后的 SPD 关键信息与可写字段偏移。</summary>
public sealed class SpdInfo
{
    public SpdMemoryType MemoryType { get; init; }
    public string ModuleBrand { get; init; } = JedecManufacturers.UnknownModuleBrand.Name;
    public string DieBrand { get; init; } = JedecManufacturers.UnknownDieBrand.Name;
    public string PartNumber { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string ProductionDate { get; init; } = "0000";
    public string FrequencyLabel { get; init; } = "-";
    public string TimingLabel { get; init; } = "-";
    /// <summary>模组容量，单位 GB；无法解析时为 0。</summary>
    public int CapacityGb { get; init; }
    public int ModuleMfgOffset { get; init; }
    public int DieMfgOffset { get; init; }
    public int SnOffset { get; init; }
    public int PartOffset { get; init; }
    public int PartNumberSize { get; init; } = 20;
    public int DateOffset { get; init; }
}

/// <summary>DDR4/DDR5 SPD 解析：品牌、料号、SN、日期、频率/时序、容量。</summary>
public static class SpdParser
{
    public static SpdInfo Parse(byte[] data, int i2cAddress = 0x50)
    {
        _ = i2cAddress; // 保留参数以兼容调用方；地址由上层维护
        if (data.Length < 256)
            return new SpdInfo();

        byte keyByte = data[2];
        bool isDdr5 = keyByte == 0x12 || data.Length >= 1024;
        bool isDdr4 = keyByte == 0x0C;

        var type = isDdr5 ? SpdMemoryType.Ddr5 : isDdr4 ? SpdMemoryType.Ddr4 : SpdMemoryType.Unknown;

        int moduleOff = isDdr5 ? 512 : 320;
        int dateOff = isDdr5 ? 515 : 323;
        int snOff = isDdr5 ? 517 : 325;
        int partOff = isDdr5 ? 521 : 329;
        int dieOff = isDdr5 ? 552 : 350;

        if (data.Length <= moduleOff + 1)
            return new SpdInfo { MemoryType = type };

        byte modCont = data[moduleOff];
        byte modCode = data[moduleOff + 1];
        byte dieCont = data.Length > dieOff + 1 ? data[dieOff] : (byte)0;
        byte dieCode = data.Length > dieOff + 1 ? data[dieOff + 1] : (byte)0;

        string date = "0000";
        if (data.Length > dateOff + 1)
            date = SpdEditorLogic.FormatProductionDate(data[dateOff], data[dateOff + 1]);

        string sn = "00000000";
        if (data.Length > snOff + 3)
            sn = $"{data[snOff]:X2}{data[snOff + 1]:X2}{data[snOff + 2]:X2}{data[snOff + 3]:X2}";

        int partSize = isDdr5 ? 30 : 20;
        string part = ReadAscii(data, partOff, partSize);
        var (freq, timing) = ReadJedecSpeed(data, type);
        int capacityGb = ReadCapacityGb(data, type);

        return new SpdInfo
        {
            MemoryType = type,
            ModuleBrand = JedecManufacturers.DecodeModuleName(modCont, modCode),
            DieBrand = JedecManufacturers.DecodeDieName(dieCont, dieCode),
            PartNumber = part.Trim(),
            SerialNumber = sn,
            ProductionDate = date,
            FrequencyLabel = freq,
            TimingLabel = timing,
            CapacityGb = capacityGb,
            ModuleMfgOffset = moduleOff,
            DieMfgOffset = dieOff,
            SnOffset = snOff,
            PartOffset = partOff,
            PartNumberSize = partSize,
            DateOffset = dateOff,
        };
    }

    /// <summary>
    /// 按 JEDEC SPD 计算模组容量（GB）。
    /// DDR5：公开实现同 nwinfo/spdr —— Byte4 密度、Byte6 I/O 宽度、Byte234 秩、Byte235 通道与总线宽。
    /// </summary>
    public static int ReadCapacityGb(byte[] data, SpdMemoryType type)
    {
        try
        {
            if (type == SpdMemoryType.Ddr4 && data.Length > 13)
                return ReadDdr4CapacityGb(data);

            if (type == SpdMemoryType.Ddr5 && data.Length > 235)
                return ReadDdr5CapacityGb(data);
        }
        catch
        {
            // ignore
        }

        return 0;
    }

    private static int ReadDdr4CapacityGb(byte[] data)
    {
        // Density per die (Mb): 256/512/1G/2G/4G/8G/16G/32G/12G/24G
        int[] densityMbTable = [256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 12288, 24576];
        int densIdx = data[4] & 0x0F;
        int densityMb = densIdx < densityMbTable.Length ? densityMbTable[densIdx] : 0;
        if (densityMb <= 0) return 0;

        int sdramWidth = (data[12] & 0x07) switch { 0 => 4, 1 => 8, 2 => 16, 3 => 32, _ => 8 };
        int ranks = ((data[12] >> 3) & 0x07) + 1;
        int busWidth = (data[13] & 0x07) switch { 0 => 8, 1 => 16, 2 => 32, 3 => 64, _ => 64 };
        int dieCount = ((data[6] >> 4) & 0x07);
        int dieMult = dieCount > 0 ? dieCount : 1;
        if (sdramWidth <= 0) return 0;

        int capacityMb = densityMb / 8 * busWidth / sdramWidth * ranks * dieMult;
        return Math.Max(0, capacityMb / 1024);
    }

    private static int ReadDdr5CapacityGb(byte[] data)
    {
        // densCode → 单 die 容量（MiB）：4Gb=512 ... 64Gb=8192
        int[] densityMiB = [0, 512, 1024, 1536, 2048, 3072, 4096, 6144, 8192];

        int channelsPerDimm = (((data[235] >> 5) & 0x03) == 1) ? 2 : 1;
        int busWidthPerChannel = 1 << ((data[235] & 0x03) + 3); // 8/16/32/64
        int pkgRanksPerChannel = 1 << ((data[234] >> 3) & 0x07); // 1/2/4/...
        bool asymmetric = ((data[234] >> 6) & 0x01) != 0;

        long moduleSizeMiB = 0;
        int maxSdram = asymmetric ? 2 : 1;
        for (int i = 1; i <= maxSdram; i++)
        {
            int densOff = i * 4;       // 4, 8
            int ioOff = densOff + 2;   // 6, 10
            if (ioOff >= data.Length) break;

            byte densByte = data[densOff];
            int densCode = densByte & 0x1F;
            if (densCode <= 0 || densCode >= densityMiB.Length) continue;

            long cur = densityMiB[densCode];

            // Die per package：bits[7:5]，2..5 → ×2/4/8/16
            int pkgCode = (densByte >> 5) & 0x07;
            if (pkgCode is > 1 and <= 5)
                cur *= 1 << (pkgCode - 1);

            cur *= channelsPerDimm;
            cur *= busWidthPerChannel;

            // I/O width：Byte6/10 bits[7:5] → ÷4/8/16/32
            int ioCode = (data[ioOff] >> 5) & 0x03;
            int ioWidth = 1 << (ioCode + 2);
            if (ioWidth <= 0) continue;
            cur /= ioWidth;

            cur *= pkgRanksPerChannel;
            moduleSizeMiB += cur;
        }

        if (moduleSizeMiB <= 0) return 0;
        return (int)(moduleSizeMiB / 1024);
    }

    /// <summary>
    /// 从 JEDEC 基础 SPD 读取默认运行频率与主时序（CL-tRCD-tRP-tRAS，单位：时钟周期 nCK）。
    /// DDR4：字节存 MTB/FTB 时间，按 JESD21-C 用 ceil(tXXmin / tCKAVGmin) 换算。
    /// </summary>
    public static (string Frequency, string Timing) ReadJedecSpeed(byte[] data, SpdMemoryType type)
    {
        try
        {
            if (type == SpdMemoryType.Ddr4 && data.Length > 125)
            {
                // t = MTB_units * 0.125ns + FTB_offset * 0.001ns  →  picoseconds
                int tckPs = SpdUtils.TicksToPsDdr4(data[18]) + SpdUtils.SignedByte(data[125]);
                if (tckPs <= 0) return ("-", "-");

                int taaPs = SpdUtils.TicksToPsDdr4(data[24]) + SpdUtils.SignedByte(data[123]);
                int trcdPs = SpdUtils.TicksToPsDdr4(data[25]) + SpdUtils.SignedByte(data[122]);
                int trpPs = SpdUtils.TicksToPsDdr4(data[26]) + SpdUtils.SignedByte(data[121]);
                int trasMtb = ((data[27] & 0x0F) << 8) | data[28];
                int trasPs = SpdUtils.TicksToPsDdr4(trasMtb);

                int cl = PsToNckJedec(taaPs, tckPs);
                int rcd = PsToNckJedec(trcdPs, tckPs);
                int rp = PsToNckJedec(trpPs, tckPs);
                int ras = PsToNckJedec(trasPs, tckPs);

                // 若 SPD 声明了支持的 CL，取满足 tAAmin 的最小受支持 CL（JEDEC 常用做法）
                cl = SnapToSupportedCasDdr4(data, cl);

                int mt = (int)Math.Round(SpdUtils.MtFromMinCyclePs(tckPs));
                return ($"DDR4-{mt}", $"{cl}-{rcd}-{rp}-{ras}");
            }

            if (type == SpdMemoryType.Ddr5 && data.Length > 36)
            {
                int minPs = SpdUtils.GetWord(data, 20);
                if (minPs <= 0) return ("-", "-");

                int mt = (int)Math.Round(SpdUtils.MtFromMinCyclePs(minPs));
                // 贴近 JEDEC 常用档（如 416ps → 4808 显示为 4800）
                mt = SnapDdr5DataRate(mt);

                int cl = SpdUtils.TimeToTicksDdr5(SpdUtils.GetWord(data, 30), minPs);
                int rcd = SpdUtils.TimeToTicksDdr5(SpdUtils.GetWord(data, 32), minPs);
                int rp = SpdUtils.TimeToTicksDdr5(SpdUtils.GetWord(data, 34), minPs);
                int ras = SpdUtils.TimeToTicksDdr5(SpdUtils.GetWord(data, 36), minPs);
                cl = SnapToSupportedCasDdr5(data, cl);
                return ($"DDR5-{mt}", $"{cl}-{rcd}-{rp}-{ras}");
            }
        }
        catch
        {
            // ignore malformed SPD timing blocks
        }

        return ("-", "-");
    }

    /// <summary>JEDEC：nXX = ceil(tXXmin / tCKAVGmin)</summary>
    private static int PsToNckJedec(int timingPs, int tckPs)
    {
        if (timingPs <= 0 || tckPs <= 0) return 0;
        return (int)Math.Ceiling(timingPs / (double)tckPs);
    }

    /// <summary>
    /// 在 Byte20~23 声明的受支持 CL 中，选择 ≥ 计算值的最小 CL。
    /// </summary>
    private static int SnapToSupportedCasDdr4(byte[] data, int calculatedCl)
    {
        if (calculatedCl <= 0 || data.Length < 24) return calculatedCl;

        int best = 0;
        for (int cl = Math.Max(7, calculatedCl); cl <= 36; cl++)
        {
            if (SpdUtils.IsClSupportedDdr4(data, 20, cl))
            {
                best = cl;
                break;
            }
        }

        if (best > 0) return best;

        // 若更高 CL 均未声明，回退到计算值本身
        return calculatedCl;
    }

    /// <summary>
    /// DDR5：Byte24~28 位图，CL 从 20 起每隔 2；取 ≥ 计算值的最小受支持偶数 CL。
    /// </summary>
    private static int SnapToSupportedCasDdr5(byte[] data, int calculatedCl)
    {
        if (calculatedCl <= 0 || data.Length < 29) return calculatedCl;

        int cl = calculatedCl;
        if ((cl & 1) != 0) cl++; // 奇数上取偶

        for (; cl <= 98; cl += 2)
        {
            int bit = (cl - 20) / 2;
            if (bit is < 0 or >= 40) continue;
            if ((data[24 + bit / 8] & (1 << (bit % 8))) != 0)
                return cl;
        }

        return calculatedCl;
    }

    /// <summary>将算出的 MT/s 吸附到最近的常见 DDR5 速率档。</summary>
    private static int SnapDdr5DataRate(int mt)
    {
        if (mt <= 0) return mt;
        ReadOnlySpan<int> rates =
        [
            3200, 3600, 4000, 4400, 4800, 5200, 5600, 6000, 6400, 6800, 7200, 7600, 8000, 8400, 8800
        ];
        int best = rates[0];
        int bestDiff = Math.Abs(mt - best);
        foreach (int r in rates)
        {
            int d = Math.Abs(mt - r);
            if (d < bestDiff)
            {
                best = r;
                bestDiff = d;
            }
        }
        // 偏差过大则保留原值（非标准超频条）
        return bestDiff <= 40 ? best : mt;
    }

    public static string ReadAscii(byte[] data, int offset, int length)
    {
        if (offset + length > data.Length) length = Math.Max(0, data.Length - offset);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte b = data[offset + i];
            chars[i] = b >= 0x20 && b <= 0x7E ? (char)b : ' ';
        }
        return new string(chars).TrimEnd();
    }

    public static string GetTypeLabel(SpdMemoryType type) => type switch
    {
        SpdMemoryType.Ddr4 => "DDR4",
        SpdMemoryType.Ddr5 => "DDR5",
        _ => "未知",
    };
}

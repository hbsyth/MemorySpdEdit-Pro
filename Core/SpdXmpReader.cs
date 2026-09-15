namespace SpdEditor.Core;

/// <summary>
/// 从 SPD BIN 解析 Intel XMP / AMD EXPO 概要（只读展示，不写回）。
/// </summary>
public static class SpdXmpReader
{
    public static string FormatReport(byte[] data)
    {
        if (data.Length == 0)
            return "当前无 SPD 数据，请先读取或载入 BIN。";

        var type = SpdParser.ResolveType(data);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"内存类型: {SpdParser.GetTypeLabel(type)}");
        sb.AppendLine($"数据长度: {data.Length} 字节");
        sb.AppendLine();

        bool any = false;
        switch (type)
        {
            case SpdMemoryType.Ddr5:
                any |= AppendDdr5Xmp(data, sb);
                any |= AppendDdr5Expo(data, sb);
                break;
            case SpdMemoryType.Ddr4:
                any |= AppendDdr4Xmp(data, sb);
                break;
            default:
                sb.AppendLine("当前类型不解析 XMP/EXPO（仅 DDR4 / DDR5）。");
                return sb.ToString().TrimEnd();
        }

        if (!any)
            sb.AppendLine("未检测到 XMP / EXPO 配置块。");

        return sb.ToString().TrimEnd();
    }

    private static bool AppendDdr5Xmp(byte[] data, System.Text.StringBuilder sb)
    {
        const int magic = 640;
        if (data.Length < 768 || data[magic] != 0x0C || data[magic + 1] != 0x4A)
            return false;

        byte enable = data.Length > 643 ? data[643] : (byte)0;
        sb.AppendLine("=== Intel XMP 3.0 ===");
        sb.AppendLine($"启用位: 0x{enable:X2} (bit0=配置1, bit1=配置2)");

        AppendSectionCrc(data, 640, 64, "XMP头 CRC", sb);
        AppendProfileName(data, 654, "配置1名称", sb);
        AppendProfileName(data, 670, "配置2名称", sb);

        if ((enable & 0x01) != 0)
            AppendDdr5XmpProfile(data, 704, 1, sb);
        else
            sb.AppendLine("配置1: 未启用");

        if ((enable & 0x02) != 0)
            AppendDdr5XmpProfile(data, 768, 2, sb);
        else
            sb.AppendLine("配置2: 未启用");

        sb.AppendLine();
        return true;
    }

    private static void AppendDdr5XmpProfile(byte[] data, int baseOff, int index, System.Text.StringBuilder sb)
    {
        if (data.Length < baseOff + 64)
        {
            sb.AppendLine($"配置{index}: 数据长度不足");
            return;
        }

        int tck = SpdUtils.GetWord(data, baseOff + 5);
        int taa = SpdUtils.GetWord(data, baseOff + 13);
        int trcd = SpdUtils.GetWord(data, baseOff + 15);
        int trp = SpdUtils.GetWord(data, baseOff + 17);
        int tras = SpdUtils.GetWord(data, baseOff + 19);
        int trc = SpdUtils.GetWord(data, baseOff + 21);

        int mt = tck > 0 ? (int)Math.Round(SpdUtils.MtFromMinCyclePs(tck)) : 0;
        int cl = tck > 0 ? SpdUtils.TimeToTicksDdr5(taa, tck) : 0;
        int rcd = tck > 0 ? SpdUtils.TimeToTicksDdr5(trcd, tck) : 0;
        int rp = tck > 0 ? SpdUtils.TimeToTicksDdr5(trp, tck) : 0;
        int ras = tck > 0 ? SpdUtils.TimeToTicksDdr5(tras, tck) : 0;

        sb.AppendLine($"配置{index}:");
        sb.AppendLine($"  频率: DDR5-{mt}  (tCK={tck} ps)");
        sb.AppendLine($"  时序: {cl}-{rcd}-{rp}-{ras}  (tRC nCK={SpdUtils.TimeToTicksDdr5(trc, tck)})");
        sb.AppendLine($"  电压: VDD={FormatDdr5Voltage(data[baseOff + 1])}  VDDQ={FormatDdr5Voltage(data[baseOff + 2])}  VPP={FormatDdr5Voltage(data[baseOff])}");
        AppendSectionCrc(data, baseOff, 64, $"  配置{index} CRC", sb);
    }

    private static bool AppendDdr5Expo(byte[] data, System.Text.StringBuilder sb)
    {
        const int off = 832;
        if (data.Length < off + 128)
            return false;
        if (data[off] != (byte)'E' || data[off + 1] != (byte)'X'
            || data[off + 2] != (byte)'P' || data[off + 3] != (byte)'O')
            return false;

        sb.AppendLine("=== AMD EXPO ===");
        AppendSectionCrc(data, off, 128, "EXPO块 CRC", sb);

        // 配置槽：842 / 882，每槽 40 字节；tCK 在 +4
        for (int i = 0; i < 2; i++)
        {
            int baseOff = 842 + i * 40;
            if (data.Length < baseOff + 14)
                continue;
            int tck = SpdUtils.GetWord(data, baseOff + 4);
            if (tck == 0)
            {
                sb.AppendLine($"配置{i + 1}: 空");
                continue;
            }

            int taa = SpdUtils.GetWord(data, baseOff + 6);
            int trcd = SpdUtils.GetWord(data, baseOff + 8);
            int trp = SpdUtils.GetWord(data, baseOff + 10);
            int tras = SpdUtils.GetWord(data, baseOff + 12);
            int mt = (int)Math.Round(SpdUtils.MtFromMinCyclePs(tck));
            int cl = SpdUtils.TimeToTicksDdr5(taa, tck);
            int rcd = SpdUtils.TimeToTicksDdr5(trcd, tck);
            int rp = SpdUtils.TimeToTicksDdr5(trp, tck);
            int ras = SpdUtils.TimeToTicksDdr5(tras, tck);

            sb.AppendLine($"配置{i + 1}:");
            sb.AppendLine($"  频率: DDR5-{mt}  (tCK={tck} ps)");
            sb.AppendLine($"  时序: {cl}-{rcd}-{rp}-{ras}");
            sb.AppendLine($"  电压: VDD={FormatDdr5Voltage(data[baseOff])}  VDDQ={FormatDdr5Voltage(data[baseOff + 1])}  VPP={FormatDdr5Voltage(data[baseOff + 2])}");
        }

        sb.AppendLine();
        return true;
    }

    private static bool AppendDdr4Xmp(byte[] data, System.Text.StringBuilder sb)
    {
        const int magic = 384;
        if (data.Length < 440 || data[magic] != 0x0C || data[magic + 1] != 0x4A)
            return false;

        byte enable = data[386];
        sb.AppendLine("=== Intel XMP 2.0 ===");
        sb.AppendLine($"启用位: 0x{enable:X2} (bit0=配置1, bit1=配置2)");
        int ver = data.Length > 387 ? data[387] : 0;
        sb.AppendLine($"版本: {(ver >> 4) & 0xF}.{(ver & 0xF)}");

        if ((enable & 0x01) != 0)
            AppendDdr4XmpProfile(data, 393, 1, sb);
        else
            sb.AppendLine("配置1: 未启用");

        if ((enable & 0x02) != 0)
            AppendDdr4XmpProfile(data, 440, 2, sb);
        else
            sb.AppendLine("配置2: 未启用");

        sb.AppendLine();
        return true;
    }

    private static void AppendDdr4XmpProfile(byte[] data, int baseOff, int index, System.Text.StringBuilder sb)
    {
        if (data.Length < baseOff + 39)
        {
            sb.AppendLine($"配置{index}: 数据长度不足");
            return;
        }

        // MTB=0.125ns；FTB 在 profile+38(tCK)、+37(tCL) 等
        int tckMtb = data[baseOff + 3];
        int tckFtb = SpdUtils.SignedByte(data[baseOff + 38]);
        int tckPs = SpdUtils.TicksToPsDdr4(tckMtb) + tckFtb;
        if (tckPs <= 0)
        {
            sb.AppendLine($"配置{index}: tCK 无效");
            return;
        }

        int clPs = SpdUtils.TicksToPsDdr4(data[baseOff + 8]) + SpdUtils.SignedByte(data[baseOff + 37]);
        int rcdPs = SpdUtils.TicksToPsDdr4(data[baseOff + 9]) + SpdUtils.SignedByte(data[baseOff + 36]);
        int rpPs = SpdUtils.TicksToPsDdr4(data[baseOff + 10]) + SpdUtils.SignedByte(data[baseOff + 35]);
        int trasMtb = ((data[baseOff + 11] & 0x0F) << 8) | data[baseOff + 12];
        int trasPs = SpdUtils.TicksToPsDdr4(trasMtb);

        int cl = (int)Math.Ceiling(clPs / (double)tckPs);
        int rcd = (int)Math.Ceiling(rcdPs / (double)tckPs);
        int rp = (int)Math.Ceiling(rpPs / (double)tckPs);
        int ras = (int)Math.Ceiling(trasPs / (double)tckPs);
        int mt = (int)Math.Round(SpdUtils.MtFromMinCyclePs(tckPs));

        byte vRaw = data[baseOff];
        double volts = ((vRaw & 0x80) != 0 ? 1.0 : 0.0) + (vRaw & 0x7F) * 0.01;

        sb.AppendLine($"配置{index}:");
        sb.AppendLine($"  频率: DDR4-{mt}  (tCK={tckPs} ps)");
        sb.AppendLine($"  时序: {cl}-{rcd}-{rp}-{ras}");
        sb.AppendLine($"  电压: VDD≈{volts:F2} V");
    }

    private static void AppendProfileName(byte[] data, int offset, string label, System.Text.StringBuilder sb)
    {
        if (data.Length < offset + 16) return;
        var chars = new char[16];
        int n = 0;
        for (int i = 0; i < 16; i++)
        {
            byte b = data[offset + i];
            if (b is >= 0x20 and <= 0x7E)
                chars[n++] = (char)b;
            else if (b == 0)
                break;
            else
                chars[n++] = ' ';
        }
        string name = new string(chars, 0, n).Trim();
        if (name.Length > 0)
            sb.AppendLine($"{label}: {name}");
    }

    private static void AppendSectionCrc(byte[] data, int start, int blockLen, string label, System.Text.StringBuilder sb)
    {
        if (data.Length < start + blockLen) return;
        ushort expect = SpdUtils.Crc16Xmodem(data.AsSpan(start, blockLen - 2));
        ushort actual = (ushort)SpdUtils.GetWord(data, start + blockLen - 2);
        sb.AppendLine($"{label}: {(expect == actual ? "OK" : "FAIL")} (期望 {expect:X4} / 实际 {actual:X4})");
    }

    /// <summary>DDR5 配置电压编码：高 3 位为整伏，低 5 位为 50mV 步进。</summary>
    private static string FormatDdr5Voltage(byte raw)
    {
        int mv = (raw >> 5) * 1000 + (raw & 0x1F) * 50;
        return $"{mv / 1000.0:F2} V";
    }
}

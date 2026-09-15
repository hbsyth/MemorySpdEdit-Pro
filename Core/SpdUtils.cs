namespace SpdEditor.Core;

/// <summary>
/// SPD 校验与时序换算工具（DDR4/DDR5 JEDEC 常用算法）。
/// 仅保留当前解析/写回路径使用的 API。
/// </summary>
public static class SpdUtils
{
    /// <summary>DDR4 MTB 单位（ns）。</summary>
    public const double Ddr4MtbNs = 0.125;

    /// <summary>CRC-16/XMODEM，用于 DDR4/DDR5 SPD 校验字。</summary>
    public static ushort Crc16Xmodem(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)(((crc << 1) ^ 0x1021) & 0xFFFF)
                    : (ushort)((crc << 1) & 0xFFFF);
            }
        }
        return crc;
    }

    public static int BytesToUshort(byte lsb, byte msb) => (msb << 8) | lsb;

    public static (byte Lsb, byte Msb) UshortToBytes(int value)
    {
        value &= 0xFFFF;
        return ((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF));
    }

    public static int GetWord(byte[] data, int offset) =>
        BytesToUshort(data[offset], data[offset + 1]);

    public static void SetWord(byte[] data, int offset, int value)
    {
        var (lo, hi) = UshortToBytes(value);
        data[offset] = lo;
        data[offset + 1] = hi;
    }

    /// <summary>有符号字节（FTB 微调等）。</summary>
    public static int SignedByte(byte val) => val < 128 ? val : val - 256;

    /// <summary>DDR4 MTB ticks → 皮秒（精确：ticks × 125）。</summary>
    public static int TicksToPsDdr4(int ticks) =>
        ticks <= 0 ? 0 : ticks * 125;

    /// <summary>DDR4 皮秒 → MTB ticks（向上取整，对齐 JEDEC / Python ns_to_ticks）。</summary>
    public static int PsToMtbTicksDdr4(int ps)
    {
        if (ps <= 0) return 0;
        return (int)(ps / (Ddr4MtbNs * 1000.0) + 0.9999);
    }

    /// <summary>DDR4 皮秒 → (MTB, FTB)，保证 FTB 可写入有符号字节。</summary>
    public static (int Ticks, int Fc) PsToTicksFcDdr4(int ps) =>
        SpdTimingRules.FitPsToMtbFtb(ps);

    /// <summary>DDR4 XMP 电压字节 → 厘伏（bit7=整数伏特，bits6-0=百分之一伏特）。</summary>
    public static int Ddr4VoltageByteToCv(int raw)
    {
        int hundredths = raw & 0x7F;
        int ones = (raw & 0x80) >> 7;
        return ones * 100 + hundredths;
    }

    /// <summary>厘伏 → DDR4 XMP 电压字节。</summary>
    public static byte Ddr4VoltageCvToByte(int cv)
    {
        cv = SpdTimingRules.ClampVoltageCv(cv);
        // 编码上限约 1.27V（bit7 + 0x7F）；超过则钳制到可表示最大值
        if (cv > 227) cv = 227;
        int ones = cv >= 100 ? 1 : 0;
        int hundredths = cv >= 100 ? cv - 100 : cv;
        return (byte)((ones != 0 ? 0x80 : 0x00) | (hundredths & 0x7F));
    }

    /// <summary>
    /// DDR5：时间(ps) → nCK（JESD400-5B，含 0.30% 修正因子）。
    /// nCK = floor(t×0.997/tCK) + 1
    /// </summary>
    public static int TimeToTicksDdr5(int timePs, int minCycleTime)
    {
        if (minCycleTime <= 0 || timePs <= 0) return 0;
        const int correctionFactor = 3; // 0.30% × 1000
        double temp = timePs * (1000.0 - correctionFactor);
        double tempNck = temp / minCycleTime;
        tempNck += 1000;
        int nck = (int)(tempNck / 1000);
        return nck < 0 ? 0 : nck;
    }

    /// <summary>DDR5：nCK × tCK → 时间(ps)。</summary>
    public static int TicksToTimeDdr5(int ticks, int minCycleTime) =>
        minCycleTime <= 0 || ticks <= 0 ? 0 : ticks * minCycleTime;

    /// <summary>SPD 电压字节 → 厘伏（centivolts；110 = 1.10 V）。</summary>
    public static int VoltageByteToCv(int val) => (val >> 5) * 100 + (val & 0x1F) * 5;

    /// <summary>厘伏 → SPD 电压字节（高 3 位 ones×100，低 5 位 ×5）。</summary>
    public static byte VoltageCvToByte(int cv)
    {
        cv = SpdTimingRules.ClampVoltageCv(cv);
        int ones = Math.Clamp(cv / 100, 0, 7);
        int hundredths = Math.Clamp(cv % 100, 0, 155);
        return (byte)(((ones << 5) + (hundredths / 5)) & 0xFF);
    }

    /// <summary>最小时钟周期(ps) → 传输速率 MT/s（DDR 双沿）。</summary>
    public static double MtFromMinCyclePs(int minCyclePs) =>
        minCyclePs > 0 ? 2_000_000.0 / minCyclePs : 0;

    /// <summary>DDR4 CAS Latency 是否在 Byte20–23 声明的支持位图中。</summary>
    public static bool IsClSupportedDdr4(byte[] data, int clOffset, int cl)
    {
        if (cl < 7 || cl > 36) return false;
        int bit = cl - 7;
        return (data[clOffset + bit / 8] & (1 << (bit % 8))) != 0;
    }
}

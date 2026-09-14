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

    /// <summary>DDR4 MTB ticks → 皮秒。</summary>
    public static int TicksToPsDdr4(int ticks) => (int)Math.Round(ticks * Ddr4MtbNs * 1000);

    /// <summary>DDR5：时间(ps) / 最小周期 → 时钟周期数（含 JEDEC 修正因子）。</summary>
    public static int TimeToTicksDdr5(int timePs, int minCycleTime)
    {
        if (minCycleTime <= 0) return 0;
        const int correctionFactor = 3;
        double temp = timePs * (1000.0 - correctionFactor);
        double tempNck = temp / minCycleTime;
        tempNck += 1000;
        return (int)(tempNck / 1000);
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

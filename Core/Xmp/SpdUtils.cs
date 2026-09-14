namespace SpdEditor.Core.Xmp;

/// <summary>
/// CRC、电压、时序转换（移植自 ddrxmpeditor-pro / ddr5_utils.py）
/// </summary>
public static class SpdUtils
{
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

    public static int TimeToTicksDdr5(int timePs, int minCycleTime)
    {
        if (minCycleTime <= 0) return 0;
        const int correctionFactor = 3;
        double temp = timePs * (1000.0 - correctionFactor);
        double tempNck = temp / minCycleTime;
        tempNck += 1000;
        return (int)(tempNck / 1000);
    }

    public static int TicksToTimeDdr5(int ticks, int minCycleTime) =>
        minCycleTime <= 0 ? 0 : ticks * minCycleTime;

    public static int VoltageByteToCentivolts(byte val)
    {
        int ones = val >> 5;
        int hundredths = val & 0x1F;
        return ones * 100 + hundredths * 5;
    }

    public static byte VoltageCentivoltsToByte(int cv)
    {
        int ones = cv / 100;
        int hundredths = cv % 100;
        return (byte)(((ones << 5) + (hundredths / 5)) & 0xFF);
    }

    public static bool GetBit(byte bits, int bitNumber) => (bits & (1 << bitNumber)) != 0;

    public static byte SetBit(byte bits, int bitNumber, bool value) =>
        value ? (byte)(bits | (1 << bitNumber)) : (byte)(bits & (0xFF ^ (1 << bitNumber)));

    public static (int ByteIdx, int BitPos) ClToByteBit(int cl)
    {
        if (cl < 20 || cl > 98 || cl % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(cl));
        int bit = (cl - 20) / 2;
        return (bit / 8, bit % 8);
    }

    public static bool IsClSupportedDdr5(byte[] clBytes, int cl)
    {
        var (byteIdx, bitPos) = ClToByteBit(cl);
        return (clBytes[byteIdx] & (1 << bitPos)) != 0;
    }

    public static void SetClSupportedDdr5(byte[] data, int clOffset, int cl, bool supported)
    {
        var (byteIdx, bitPos) = ClToByteBit(cl);
        int offset = clOffset + byteIdx;
        int mask = 1 << bitPos;
        data[offset] = supported
            ? (byte)(data[offset] | mask)
            : (byte)(data[offset] & ~mask);
    }

    public static bool IsClSupportedDdr4(byte[] data, int clOffset, int cl)
    {
        if (cl < 7 || cl > 36) return false;
        int bit = cl - 7;
        return (data[clOffset + bit / 8] & (1 << (bit % 8))) != 0;
    }

    public static void SetClSupportedDdr4(byte[] data, int clOffset, int cl, bool supported)
    {
        if (cl < 7 || cl > 36) return;
        int bit = cl - 7;
        int offset = clOffset + bit / 8;
        int mask = 1 << (bit % 8);
        data[offset] = supported
            ? (byte)(data[offset] | mask)
            : (byte)(data[offset] & ~mask);
    }

    public const double Ddr4MtbNs = 0.125;
    public const double Ddr4FtbNs = 0.001;

    public static int NsToTicksDdr4(double ns) => (int)(ns / Ddr4MtbNs + 0.9999);

    public static int PsToTicksFcDdr4(int ps, out int fc)
    {
        int ticks = (int)(ps / (Ddr4MtbNs * 1000) + 0.9999);
        fc = ps - (int)Math.Round(ticks * Ddr4MtbNs * 1000);
        return ticks;
    }

    public static int TicksToPsDdr4(int ticks) => (int)Math.Round(ticks * Ddr4MtbNs * 1000);

    public static int SignedByte(byte val) => val < 128 ? val : val - 256;

    public static int GetWord(byte[] data, int offset) =>
        BytesToUshort(data[offset], data[offset + 1]);

    public static void SetWord(byte[] data, int offset, int value)
    {
        var (lo, hi) = UshortToBytes(value);
        data[offset] = lo;
        data[offset + 1] = hi;
    }

    public static int GetTicksDdr5(byte[] data, int offset, int minCycleTime, int multiplier = 1)
    {
        int raw = GetWord(data, offset) * multiplier;
        return TimeToTicksDdr5(raw, minCycleTime);
    }

    public static double FrequencyMhzFromMinCyclePs(int minCyclePs) =>
        minCyclePs > 0 ? 1_000_000.0 / minCyclePs : 0;

    public static double MtFromMinCyclePs(int minCyclePs) =>
        minCyclePs > 0 ? 2_000_000.0 / minCyclePs : 0;
}

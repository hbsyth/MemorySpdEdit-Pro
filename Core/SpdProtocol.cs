namespace SpdEditor.Core;

/// <summary>
/// SPD 读写器串口协议（移植自 spdrw/spdrw.github.io）
/// 帧格式: AA 55 [type] [deviceType] [addrHi] [addrLo] 00 [data] [crc8]
/// </summary>
public static class SpdProtocol
{
    public static readonly Dictionary<int, int> DataSizes = new()
    {
        [3] = 256,
        [4] = 512,
        [5] = 1024,
    };

    public const byte CmdWrite = 0x02;
    public const byte CmdUnlock = 0x03;
    public const byte CmdLock = 0x04;
    public const byte CmdRead = 0x05;

    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (byte)((crc >> 1) ^ 0x8C) : (byte)(crc >> 1);
            }
        }
        return crc;
    }

    public static byte[] BuildCommand(byte type, int deviceType, int address, byte data = 0)
    {
        var frame = new byte[9];
        frame[0] = 0xAA;
        frame[1] = 0x55;
        frame[2] = type;
        frame[3] = (byte)deviceType;
        frame[4] = (byte)((address >> 8) & 0xFF);
        frame[5] = (byte)(address & 0xFF);
        frame[6] = 0;
        frame[7] = data;
        // 与 spdrw 一致：CRC 覆盖 type..data（frame[2..7]）
        frame[8] = Crc8(frame.AsSpan(2, 6));
        return frame;
    }

    public static byte[]? ParseHexResponse(string line, int deviceType)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return null;

        var bytes = new List<byte>();
        for (int i = 0; i + 1 < trimmed.Length; i += 2)
        {
            if (byte.TryParse(trimmed.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                bytes.Add(b);
        }

        if (bytes.Count == 0) return null;

        if (DataSizes.TryGetValue(deviceType, out int size))
            return bytes.Take(size).ToArray();

        return bytes.ToArray();
    }

    public static int DetectDeviceType(byte[] data)
    {
        if (data.Length >= 1024) return 5;
        if (data.Length >= 512) return 4;
        if (data.Length >= 256) return 3;
        return 4;
    }
}

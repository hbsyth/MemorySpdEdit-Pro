namespace SpdEditor.Core;

/// <summary>
/// 1a2m3/SPD-Reader-Writer Arduino 固件二进制协议常量与解析。
/// 回包：'&amp;' [len] [body...] [sum(body)]；告警：'@' [type]
/// </summary>
public static class SpdArduinoProtocol
{
    public const byte HeaderResponse = (byte)'&'; // 0x26
    public const byte HeaderAlert = (byte)'@';    // 0x40
    public const int MaxBodySize = 32;
    public const int PacketMinSize = 2;
    public const int PacketMaxSize = 1 + 1 + MaxBodySize + 1;

    public static readonly ushort[] SpdSizes = [0, 256, 512, 1024];

    /// <summary>本程序实际用到的固件命令（未使用的上游枚举项已剔除）。</summary>
    public enum Command : byte
    {
        Get = 0xFF,
        Disable = 0,
        Enable = 1,
        ReadByte = 2,
        WriteByte = 3,
        WriteTest = 5,
        Ddr4Detect = 6,
        Ddr5Detect = 7,
        Size = 9,
        ScanBus = 10,
        Rswp = 15,
        Pswp = 16,
        Test = 19,
    }

    /// <summary>包体校验：简单累加和（与上游 Data.Crc 一致）。</summary>
    public static byte Checksum(ReadOnlySpan<byte> body)
    {
        byte sum = 0;
        foreach (byte b in body)
            sum += b;
        return sum;
    }

    public static bool TryParsePacket(ReadOnlySpan<byte> raw, out byte[] body)
    {
        body = [];
        if (raw.Length < PacketMinSize + 1) return false;
        if (raw[0] != HeaderResponse) return false;

        int len = raw[1];
        int total = PacketMinSize + len + 1;
        if (raw.Length < total || len > MaxBodySize) return false;

        var payload = raw.Slice(PacketMinSize, len);
        byte crc = raw[PacketMinSize + len];
        if (Checksum(payload) != crc) return false;

        body = payload.ToArray();
        return true;
    }

    public static int ResolveSpdSize(byte sizeIndex)
        => sizeIndex < SpdSizes.Length ? SpdSizes[sizeIndex] : 0;
}

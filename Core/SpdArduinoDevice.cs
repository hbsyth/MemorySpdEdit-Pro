namespace SpdEditor.Core;

/// <summary>
/// 基于 1a2m3 Arduino 固件的 SPD 读写封装。
/// </summary>
public sealed class SpdArduinoDevice
{
    private readonly SerialDeviceService _serial;

    public SpdArduinoDevice(SerialDeviceService serial) => _serial = serial;

    public byte I2cAddress { get; private set; } = 0x50;
    public int SpdSize { get; private set; }

    public bool TryProbe(out string detail)
    {
        try
        {
            byte[] body = _serial.TransactArduino([(byte)SpdArduinoProtocol.Command.Test], 2000);
            bool ok = body.Length > 0 && body[0] != 0;
            detail = ok ? "Arduino 固件通信测试通过" : "Test 回包异常";
            return ok;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    public byte[] ScanBus()
    {
        byte[] body = _serial.TransactArduino([(byte)SpdArduinoProtocol.Command.ScanBus]);
        if (body.Length == 0 || body[0] == 0) return [];

        var list = new List<byte>();
        byte mask = body[0];
        for (byte i = 0; i <= 7; i++)
        {
            if ((mask & (1 << i)) != 0)
                list.Add((byte)(0x50 + i));
        }
        return list.ToArray();
    }

    public bool DetectDdr4(byte i2c) =>
        AsBool(_serial.TransactArduino([(byte)SpdArduinoProtocol.Command.Ddr4Detect, i2c]));

    public bool DetectDdr5(byte i2c) =>
        AsBool(_serial.TransactArduino([(byte)SpdArduinoProtocol.Command.Ddr5Detect, i2c]));

    public int QuerySpdSize(byte i2c)
    {
        byte[] body = _serial.TransactArduino([(byte)SpdArduinoProtocol.Command.Size, i2c]);
        if (body.Length == 0) return 0;
        return SpdArduinoProtocol.ResolveSpdSize(body[0]);
    }

    public bool Prepare(out string message)
    {
        var addrs = ScanBus();
        if (addrs.Length == 0)
        {
            message = "I2C 总线未发现 SPD 设备（请确认内存条已插入）";
            return false;
        }

        I2cAddress = addrs[0];

        if (DetectDdr5(I2cAddress))
            SpdSize = 1024;
        else if (DetectDdr4(I2cAddress))
            SpdSize = 512;
        else
        {
            int sz = QuerySpdSize(I2cAddress);
            SpdSize = sz > 0 ? sz : 256;
        }

        message = $"发现 SPD @ 0x{I2cAddress:X2}，容量 {SpdSize} 字节" +
                  (addrs.Length > 1 ? $"（共 {addrs.Length} 个地址）" : "");
        return true;
    }

    public byte[] ReadAll(IProgress<int>? progress = null)
    {
        if (SpdSize <= 0) throw new InvalidOperationException("尚未准备 SPD 尺寸");

        var result = new byte[SpdSize];
        int offset = 0;
        while (offset < SpdSize)
        {
            byte count = (byte)Math.Min(SpdArduinoProtocol.MaxBodySize, SpdSize - offset);
            byte[] chunk = ReadBytes(I2cAddress, (ushort)offset, count);
            if (chunk.Length == 0)
                throw new IOException($"偏移 0x{offset:X4} 读取为空");
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
            progress?.Report(offset * 100 / SpdSize);
        }
        return result;
    }

    public byte[] ReadBytes(byte i2c, ushort offset, byte count)
    {
        if (count == 0 || count > SpdArduinoProtocol.MaxBodySize)
            throw new ArgumentOutOfRangeException(nameof(count));

        return _serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.ReadByte,
            i2c,
            (byte)(offset >> 8),
            (byte)offset,
            count,
        ]);
    }

    public bool WriteByte(byte i2c, ushort offset, byte value) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.WriteByte,
            i2c,
            (byte)(offset >> 8),
            (byte)offset,
            value,
        ]));

    /// <summary>查询指定 RSWP 块是否已写保护（true = 已上锁）。</summary>
    public bool GetRswp(byte i2c, byte block) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.Rswp,
            i2c,
            block,
            (byte)SpdArduinoProtocol.Command.Get,
        ]));

    /// <summary>查询是否已设置永久写保护 PSWP（true = 已上锁）。</summary>
    public bool GetPswp(byte i2c) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.Pswp,
            i2c,
            (byte)SpdArduinoProtocol.Command.Get,
        ]));

    /// <summary>对指定偏移做可写性测试（true = 可写）。</summary>
    public bool WriteTest(byte i2c, ushort offset) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.WriteTest,
            i2c,
            (byte)(offset >> 8),
            (byte)offset,
        ]));

    /// <summary>
    /// 检测 SPD 是否写保护（PSWP 或任一 RSWP 块）。
    /// deviceType: 3=DDR3, 4=DDR4, 5=DDR5。
    /// </summary>
    public bool IsWriteProtected(byte i2c, int deviceType)
    {
        if (GetPswp(i2c))
            return true;

        int blocks = deviceType >= 5 ? 16 : deviceType >= 4 ? 4 : 1;
        for (byte b = 0; b < blocks; b++)
        {
            if (GetRswp(i2c, b))
                return true;
        }

        return false;
    }

    public bool ClearRswp(byte i2c) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.Rswp,
            i2c,
            0,
            (byte)SpdArduinoProtocol.Command.Disable,
        ]));

    public bool SetRswp(byte i2c, byte block) =>
        AsBool(_serial.TransactArduino([
            (byte)SpdArduinoProtocol.Command.Rswp,
            i2c,
            block,
            (byte)SpdArduinoProtocol.Command.Enable,
        ]));

    private static bool AsBool(byte[] body) => body.Length > 0 && body[0] != 0;
}

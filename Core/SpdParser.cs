namespace SpdEditor.Core;

public enum SpdMemoryType
{
    Unknown,
    Ddr4,
    Ddr5,
}

public sealed class SpdInfo
{
    public SpdMemoryType MemoryType { get; init; }
    public int I2cAddress { get; init; } = 0x50;
    public string ModuleBrand { get; init; } = "未知";
    public string DieBrand { get; init; } = "未知";
    public string PartNumber { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string ProductionDate { get; init; } = "0000";
    public int ModuleMfgOffset { get; init; }
    public int DieMfgOffset { get; init; }
    public int SnOffset { get; init; }
    public int PartOffset { get; init; }
    public int PartNumberSize { get; init; } = 20;
    public int DateOffset { get; init; }
}

public static class SpdParser
{
    public static SpdInfo Parse(byte[] data, int i2cAddress = 0x50)
    {
        if (data.Length < 256)
            return new SpdInfo { I2cAddress = i2cAddress };

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
            return new SpdInfo { MemoryType = type, I2cAddress = i2cAddress };

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

        return new SpdInfo
        {
            MemoryType = type,
            I2cAddress = i2cAddress,
            ModuleBrand = JedecManufacturers.DecodeName(modCont, modCode),
            DieBrand = JedecManufacturers.DecodeName(dieCont, dieCode),
            PartNumber = part.Trim(),
            SerialNumber = sn,
            ProductionDate = date,
            ModuleMfgOffset = moduleOff,
            DieMfgOffset = dieOff,
            SnOffset = snOff,
            PartOffset = partOff,
            PartNumberSize = partSize,
            DateOffset = dateOff,
        };
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

namespace SpdEditor.Core;

/// <summary>
/// JEDEC SPD 写前校验：类型字节、标准长度、CRC 覆盖与算法。
/// 由 <see cref="SpdEditorLogic.FinalizeForFlash"/> 在重算 CRC 后调用。
/// </summary>
public static class SpdJedecCompliance
{
    /// <summary>
    /// 写前校验：Byte2 类型、映像长度、JEDEC/XMP/EXPO CRC。
    /// 不修改数据；CRC 纠错请先走 <see cref="SpdEditorLogic.EnsureChecksums"/>。
    /// </summary>
    public static bool ValidateBeforeWrite(byte[] data, SpdMemoryType type, out string report)
    {
        var parts = new List<string>();
        bool ok = true;

        if (data is null || data.Length == 0)
        {
            report = "无 SPD 数据";
            return false;
        }

        type = SpdEditorLogic.ResolveMemoryType(data, type);
        byte expectedKey = type switch
        {
            SpdMemoryType.Ddr3 => (byte)0x0B,
            SpdMemoryType.Ddr4 => (byte)0x0C,
            SpdMemoryType.Ddr5 => (byte)0x12,
            _ => (byte)0,
        };

        if (expectedKey == 0)
        {
            parts.Add("类型未知");
            ok = false;
        }
        else if (data.Length < 3)
        {
            parts.Add("长度不足以读取 Byte2");
            ok = false;
        }
        else if (data[2] != expectedKey)
        {
            parts.Add($"Byte2=0x{data[2]:X2} 与类型 {SpdParser.GetTypeLabel(type)}(期望 0x{expectedKey:X2}) 不符");
            ok = false;
        }
        else
        {
            parts.Add($"Byte2 OK (0x{expectedKey:X2})");
        }

        int need = SpdEditorLogic.GetStandardLength(type);
        if (need > 0 && data.Length < need)
        {
            parts.Add($"长度 {data.Length} < 标准 {need}");
            ok = false;
        }
        else if (need > 0)
        {
            parts.Add($"长度 OK ({data.Length})");
        }

        if (type == SpdMemoryType.Ddr5 && data.Length >= 642
            && data[640] == 0x0C && data[641] == 0x4A)
            parts.Add("XMP3 魔数 0x0C4A @640");

        if (type == SpdMemoryType.Ddr4 && data.Length >= 386
            && data[0x180] == 0x0C && data[0x181] == 0x4A)
            parts.Add("XMP2 魔数 0x0C4A @0x180");

        if (type == SpdMemoryType.Ddr5 && data.Length >= 836
            && data[832] == (byte)'E' && data[833] == (byte)'X'
            && data[834] == (byte)'P' && data[835] == (byte)'O')
            parts.Add("EXPO 魔数 @832");

        if (!SpdEditorLogic.VerifyChecksums(data, type, out string crcReport))
        {
            parts.Add($"CRC 校验失败：{crcReport}");
            ok = false;
        }
        else
        {
            parts.Add($"CRC OK：{crcReport}");
        }

        report = string.Join("；", parts);
        return ok;
    }
}

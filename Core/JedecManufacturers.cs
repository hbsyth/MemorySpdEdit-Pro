namespace SpdEditor.Core;

public readonly record struct ManufacturerEntry(byte Continuation, byte Code, string Name);

/// <summary>
/// JEDEC JEP106 制造商 ID 映射
/// </summary>
public static class JedecManufacturers
{
    public static readonly ManufacturerEntry[] Brands =
    [
        new(0x80, 0x2C, "镁光 (Micron)"),
        new(0x80, 0xAD, "海力士 (SK Hynix)"),
        new(0x80, 0xCE, "三星 (Samsung)"),
        new(0x80, 0x98, "金士顿 (Kingston)"),
        new(0x80, 0x9E, "芝奇 (G.Skill)"),
        new(0x80, 0x04, "海盗船 (Corsair)"),
        new(0x80, 0x0C, "英睿达 (Crucial)"),
        new(0x80, 0xB3, "雷克沙 (Lexar)"),
        new(0x80, 0x89, "十铨 (TeamGroup)"),
        new(0x80, 0x94, "光威 (Gloway)"),
        new(0x80, 0x01, "江波龙 (Longsys)"),
        new(0x00, 0x00, "无 / 清零"),
    ];

    public static string DecodeName(byte continuation, byte code)
    {
        if (continuation == 0 && code == 0) return "未知";
        var match = Brands.FirstOrDefault(b => b.Continuation == continuation && b.Code == code);
        return match.Name ?? $"0x{continuation:X2}{code:X2}";
    }

    public static ManufacturerEntry GetByIndex(int index)
    {
        if (index < 0 || index >= Brands.Length) return Brands[0];
        return Brands[index];
    }

    public static int FindIndex(byte continuation, byte code)
    {
        for (int i = 0; i < Brands.Length; i++)
        {
            if (Brands[i].Continuation == continuation && Brands[i].Code == code)
                return i;
        }
        return 0;
    }
}

namespace SpdEditor.Core;

public enum Ddr5ProfileKind
{
    Jedec,
    Xmp,
    Expo,
}

/// <summary>DDR5 配置页展示/编辑模型。</summary>
public sealed class Ddr5ProfileView
{
    public string TabTitle { get; init; } = "";
    public Ddr5ProfileKind Kind { get; init; }
    public int BaseOffset { get; init; } = -1;
    public bool Writable { get; init; }
    public bool Present { get; init; }
    public bool Enabled { get; init; }
    public string ProfileName { get; init; } = "";
    public int TckPs { get; init; }
    public int ClockMhz => TckPs > 0 ? (int)Math.Round(1_000_000.0 / TckPs) : 0;
    public int MtPerSec => TckPs > 0 ? (int)Math.Round(SpdUtils.MtFromMinCyclePs(TckPs)) : 0;

    public int VddRaw { get; init; }
    public int VddqRaw { get; init; }
    public int VppRaw { get; init; }
    public int VmemRaw { get; init; }
    public int CommandRate { get; init; }
    public bool IntelBoost { get; init; }
    public bool RealTimeOc { get; init; }

    public HashSet<int> SupportedCas { get; init; } = [];

    public int TclPs { get; init; }
    public int TrcdPs { get; init; }
    public int TrpPs { get; init; }
    public int TrasPs { get; init; }
    public int TrcPs { get; init; }
    public int TwrPs { get; init; }
    public int Trfc1Ns { get; init; }
    public int Trfc2Ns { get; init; }
    public int TrfcsbNs { get; init; }

    public int TrrdlPs { get; init; }
    public int TrrdlMin { get; init; }
    public int TccdLPs { get; init; }
    public int TccdLMin { get; init; }
    public int TccdLWrPs { get; init; }
    public int TccdLWrMin { get; init; }
    public int TccdLWr2Ps { get; init; }
    public int TccdLWr2Min { get; init; }
    public int TfawPs { get; init; }
    public int TfawMin { get; init; }
    public int TccdLWtrPs { get; init; }
    public int TccdLWtrMin { get; init; }
    public int TccdSWtrPs { get; init; }
    public int TccdSWtrMin { get; init; }
    public int TrtpPs { get; init; }
    public int TrtpMin { get; init; }

    public int Nck(int timePs) =>
        TckPs > 0 && timePs > 0 ? SpdUtils.TimeToTicksDdr5(timePs, TckPs) : 0;

    public int NckFromNs(int timeNs) => Nck(timeNs * 1000);

    public static string FormatVoltage(int raw)
    {
        int cv = SpdUtils.VoltageByteToCv(raw);
        return $"{cv / 100.0:F2} V";
    }

    public static int TckPsFromMt(int mt) =>
        mt > 0 ? (int)Math.Round(2_000_000.0 / mt) : 0;
}

/// <summary>窗体收集后的可写回字段。</summary>
public sealed class Ddr5ProfileEdit
{
    public int TckPs { get; set; }
    public HashSet<int> SupportedCas { get; set; } = [];
    public int TclPs { get; set; }
    public int TrcdPs { get; set; }
    public int TrpPs { get; set; }
    public int TrasPs { get; set; }
    public int TrcPs { get; set; }
    public int TwrPs { get; set; }
    public int Trfc1Ns { get; set; }
    public int Trfc2Ns { get; set; }
    public int TrfcsbNs { get; set; }
    public int TrrdlPs { get; set; }
    public int TrrdlMin { get; set; }
    public int TccdLPs { get; set; }
    public int TccdLMin { get; set; }
    public int TccdLWrPs { get; set; }
    public int TccdLWrMin { get; set; }
    public int TccdLWr2Ps { get; set; }
    public int TccdLWr2Min { get; set; }
    public int TfawPs { get; set; }
    public int TfawMin { get; set; }
    public int TccdLWtrPs { get; set; }
    public int TccdLWtrMin { get; set; }
    public int TccdSWtrPs { get; set; }
    public int TccdSWtrMin { get; set; }
    public int TrtpPs { get; set; }
    public int TrtpMin { get; set; }

    /// <summary>Centivolts: 110 = 1.10 V.</summary>
    public int VddCv { get; set; }
    public int VddqCv { get; set; }
    public int VppCv { get; set; }
    public int VmemCv { get; set; }
    public int CommandRate { get; set; }
    public bool IntelBoost { get; set; }
    public bool RealTimeOc { get; set; }
    public string ProfileName { get; set; } = "";
}

/// <summary>从 DDR5 SPD 映像加载/写回各配置页。</summary>
public static class Ddr5ProfileLoader
{
    public const int XmpHeader = 640;
    public const int XmpP1 = 704;
    public const int XmpP2 = 768;
    public const int XmpP3 = 832;
    public const int XmpUser1 = 896;
    public const int XmpUser2 = 960;
    public const int ExpoBase = 832;
    public const int ExpoP1 = 842;
    public const int ExpoP2 = 882;

    public static bool HasXmp(byte[] data) =>
        data.Length >= 642 && data[640] == 0x0C && data[641] == 0x4A;

    public static bool HasExpo(byte[] data) =>
        data.Length >= 836
        && data[832] == (byte)'E' && data[833] == (byte)'X'
        && data[834] == (byte)'P' && data[835] == (byte)'O';

    public static IReadOnlyList<Ddr5ProfileView> LoadAll(byte[] data)
    {
        var list = new List<Ddr5ProfileView> { LoadJedec(data) };

        bool expo = HasExpo(data);
        bool xmp = HasXmp(data);
        byte enable = xmp && data.Length > 643 ? data[643] : (byte)0;

        list.Add(LoadXmpProfile(data, XmpP1, "XMP 1", ReadName(data, 654), (enable & 1) != 0, xmp));
        list.Add(LoadXmpProfile(data, XmpP2, "XMP 2", ReadName(data, 670), (enable & 2) != 0, xmp));
        if (!expo)
            list.Add(LoadXmpProfile(data, XmpP3, "XMP 3", ReadName(data, 686), (enable & 4) != 0, xmp && data.Length >= XmpP3 + 64));
        else
            list.Add(new Ddr5ProfileView { TabTitle = "XMP 3", Kind = Ddr5ProfileKind.Xmp, Present = false });

        if (!expo)
            list.Add(LoadXmpProfile(data, XmpUser1, "用户预设XMP1", "", false, xmp && data.Length >= XmpUser1 + 64));
        else
            list.Add(new Ddr5ProfileView { TabTitle = "用户预设XMP1", Kind = Ddr5ProfileKind.Xmp, Present = false });

        list.Add(LoadXmpProfile(data, XmpUser2, "用户预设XMP2", "", false, xmp && data.Length >= XmpUser2 + 64));

        if (expo)
        {
            byte en = data[837];
            list.Add(LoadExpoProfile(data, ExpoP1, "EXPO 1", (en & 0x01) != 0));
            list.Add(LoadExpoProfile(data, ExpoP2, "EXPO 2", (en & 0x10) != 0));
        }
        else
        {
            list.Add(new Ddr5ProfileView { TabTitle = "EXPO 1", Kind = Ddr5ProfileKind.Expo, Present = false });
            list.Add(new Ddr5ProfileView { TabTitle = "EXPO 2", Kind = Ddr5ProfileKind.Expo, Present = false });
        }

        return list;
    }

    public static void SetXmpEnableBits(byte[] data, bool p1, bool p2, bool p3)
    {
        if (!HasXmp(data) || data.Length <= 643) return;
        byte v = data[643];
        v = (byte)((v & ~0x07) | (p1 ? 1 : 0) | (p2 ? 2 : 0) | (p3 ? 4 : 0));
        data[643] = v;
    }

    public static bool TryWriteProfile(byte[] data, Ddr5ProfileView meta, Ddr5ProfileEdit edit, out string error)
    {
        error = "";
        if (!meta.Writable || meta.BaseOffset < 0)
        {
            error = $"{meta.TabTitle} 不可写回";
            return false;
        }

        SpdTimingRules.NormalizeDdr5(edit);

        bool ok = meta.Kind switch
        {
            Ddr5ProfileKind.Jedec => WriteJedec(data, edit, out error),
            Ddr5ProfileKind.Xmp => WriteXmp(data, meta.BaseOffset, edit, out error),
            Ddr5ProfileKind.Expo => WriteExpo(data, meta.BaseOffset, edit, out error),
            _ => false,
        };
        if (ok && meta.Kind == Ddr5ProfileKind.Xmp)
            TryWriteXmpProfileName(data, meta.BaseOffset, edit.ProfileName);
        return ok;
    }

    /// <summary>写回 XMP Header 中 P1/P2/P3 的 16 字节名称。</summary>
    private static void TryWriteXmpProfileName(byte[] data, int profileBase, string name)
    {
        int nameOff = profileBase switch
        {
            XmpP1 => 654,
            XmpP2 => 670,
            XmpP3 => 686,
            _ => -1,
        };
        if (nameOff < 0 || data.Length < nameOff + 16) return;
        var bytes = new byte[16];
        string s = name ?? "";
        for (int i = 0; i < 16 && i < s.Length; i++)
        {
            char c = s[i];
            bytes[i] = c is >= (char)0x20 and <= (char)0x7E ? (byte)c : (byte)' ';
        }
        Array.Copy(bytes, 0, data, nameOff, 16);
    }

    private static bool WriteJedec(byte[] data, Ddr5ProfileEdit e, out string error)
    {
        error = "";
        // JEDEC DDR5 SPD：次要时序一直到 tRTP lower limit @ 93（JESD400-5 / ddrxmpeditor-pro）
        if (data.Length < 94) { error = "SPD 长度不足"; return false; }
        SpdUtils.SetWord(data, 20, ClampU16(e.TckPs));
        WriteCasBitmap(data, 24, 5, e.SupportedCas);
        SpdUtils.SetWord(data, 30, ClampU16(e.TclPs));
        SpdUtils.SetWord(data, 32, ClampU16(e.TrcdPs));
        SpdUtils.SetWord(data, 34, ClampU16(e.TrpPs));
        SpdUtils.SetWord(data, 36, ClampU16(e.TrasPs));
        SpdUtils.SetWord(data, 38, ClampU16(e.TrcPs));
        SpdUtils.SetWord(data, 40, ClampU16(e.TwrPs));
        SpdUtils.SetWord(data, 42, ClampU16(e.Trfc1Ns));
        SpdUtils.SetWord(data, 44, ClampU16(e.Trfc2Ns));
        SpdUtils.SetWord(data, 46, ClampU16(e.TrfcsbNs));
        SpdUtils.SetWord(data, 70, ClampU16(e.TrrdlPs));
        data[72] = ClampU8(e.TrrdlMin);
        SpdUtils.SetWord(data, 73, ClampU16(e.TccdLPs));
        data[75] = ClampU8(e.TccdLMin);
        SpdUtils.SetWord(data, 76, ClampU16(e.TccdLWrPs));
        data[78] = ClampU8(e.TccdLWrMin);
        SpdUtils.SetWord(data, 79, ClampU16(e.TccdLWr2Ps));
        data[81] = ClampU8(e.TccdLWr2Min);
        SpdUtils.SetWord(data, 82, ClampU16(e.TfawPs));
        data[84] = ClampU8(e.TfawMin);
        SpdUtils.SetWord(data, 85, ClampU16(e.TccdLWtrPs));
        data[87] = ClampU8(e.TccdLWtrMin);
        SpdUtils.SetWord(data, 88, ClampU16(e.TccdSWtrPs));
        data[90] = ClampU8(e.TccdSWtrMin);
        SpdUtils.SetWord(data, 91, ClampU16(e.TrtpPs));
        data[93] = ClampU8(e.TrtpMin);
        return true;
    }

    private static bool WriteXmp(byte[] data, int baseOff, Ddr5ProfileEdit e, out string error)
    {
        error = "";
        if (data.Length < baseOff + 64) { error = "XMP 块长度不足"; return false; }
        data[baseOff + 0] = SpdUtils.VoltageCvToByte(e.VppCv);
        data[baseOff + 1] = SpdUtils.VoltageCvToByte(e.VddCv);
        data[baseOff + 2] = SpdUtils.VoltageCvToByte(e.VddqCv);
        data[baseOff + 4] = SpdUtils.VoltageCvToByte(e.VmemCv);
        SpdUtils.SetWord(data, baseOff + 5, ClampU16(e.TckPs));
        WriteCasBitmap(data, baseOff + 7, 5, e.SupportedCas);
        SpdUtils.SetWord(data, baseOff + 13, ClampU16(e.TclPs));
        SpdUtils.SetWord(data, baseOff + 15, ClampU16(e.TrcdPs));
        SpdUtils.SetWord(data, baseOff + 17, ClampU16(e.TrpPs));
        SpdUtils.SetWord(data, baseOff + 19, ClampU16(e.TrasPs));
        SpdUtils.SetWord(data, baseOff + 21, ClampU16(e.TrcPs));
        SpdUtils.SetWord(data, baseOff + 23, ClampU16(e.TwrPs));
        SpdUtils.SetWord(data, baseOff + 25, ClampU16(e.Trfc1Ns));
        SpdUtils.SetWord(data, baseOff + 27, ClampU16(e.Trfc2Ns));
        SpdUtils.SetWord(data, baseOff + 29, ClampU16(e.TrfcsbNs));
        SpdUtils.SetWord(data, baseOff + 31, ClampU16(e.TrrdlPs));
        data[baseOff + 33] = ClampU8(e.TrrdlMin);
        SpdUtils.SetWord(data, baseOff + 34, ClampU16(e.TccdLWrPs));
        data[baseOff + 36] = ClampU8(e.TccdLWrMin);
        SpdUtils.SetWord(data, baseOff + 37, ClampU16(e.TccdLWr2Ps));
        data[baseOff + 39] = ClampU8(e.TccdLWr2Min);
        SpdUtils.SetWord(data, baseOff + 40, ClampU16(e.TccdLWtrPs));
        data[baseOff + 42] = ClampU8(e.TccdLWtrMin);
        SpdUtils.SetWord(data, baseOff + 43, ClampU16(e.TccdSWtrPs));
        data[baseOff + 45] = ClampU8(e.TccdSWtrMin);
        SpdUtils.SetWord(data, baseOff + 46, ClampU16(e.TccdLPs));
        data[baseOff + 48] = ClampU8(e.TccdLMin);
        SpdUtils.SetWord(data, baseOff + 49, ClampU16(e.TrtpPs));
        data[baseOff + 51] = ClampU8(e.TrtpMin);
        SpdUtils.SetWord(data, baseOff + 52, ClampU16(e.TfawPs));
        data[baseOff + 54] = ClampU8(e.TfawMin);
        byte boost = data[baseOff + 59];
        boost = (byte)((boost & ~0x03) | (e.IntelBoost ? 1 : 0) | (e.RealTimeOc ? 2 : 0));
        data[baseOff + 59] = boost;
        data[baseOff + 60] = (byte)((data[baseOff + 60] & 0xF0) | (e.CommandRate & 0x0F));
        return true;
    }

    private static bool WriteExpo(byte[] data, int baseOff, Ddr5ProfileEdit e, out string error)
    {
        error = "";
        if (data.Length < baseOff + 40) { error = "EXPO 块长度不足"; return false; }
        data[baseOff + 0] = SpdUtils.VoltageCvToByte(e.VddCv);
        data[baseOff + 1] = SpdUtils.VoltageCvToByte(e.VddqCv);
        data[baseOff + 2] = SpdUtils.VoltageCvToByte(e.VppCv);
        SpdUtils.SetWord(data, baseOff + 4, ClampU16(e.TckPs));
        SpdUtils.SetWord(data, baseOff + 6, ClampU16(e.TclPs));
        SpdUtils.SetWord(data, baseOff + 8, ClampU16(e.TrcdPs));
        SpdUtils.SetWord(data, baseOff + 10, ClampU16(e.TrpPs));
        SpdUtils.SetWord(data, baseOff + 12, ClampU16(e.TrasPs));
        SpdUtils.SetWord(data, baseOff + 14, ClampU16(e.TrcPs));
        SpdUtils.SetWord(data, baseOff + 16, ClampU16(e.TwrPs));
        SpdUtils.SetWord(data, baseOff + 18, ClampU16(e.Trfc1Ns));
        SpdUtils.SetWord(data, baseOff + 20, ClampU16(e.Trfc2Ns));
        SpdUtils.SetWord(data, baseOff + 22, ClampU16(e.TrfcsbNs));
        SpdUtils.SetWord(data, baseOff + 24, ClampU16(e.TrrdlPs));
        SpdUtils.SetWord(data, baseOff + 26, ClampU16(e.TccdLPs));
        SpdUtils.SetWord(data, baseOff + 28, ClampU16(e.TccdLWrPs));
        SpdUtils.SetWord(data, baseOff + 30, ClampU16(e.TccdLWr2Ps));
        SpdUtils.SetWord(data, baseOff + 32, ClampU16(e.TfawPs));
        SpdUtils.SetWord(data, baseOff + 34, ClampU16(e.TccdLWtrPs));
        SpdUtils.SetWord(data, baseOff + 36, ClampU16(e.TccdSWtrPs));
        SpdUtils.SetWord(data, baseOff + 38, ClampU16(e.TrtpPs));
        return true;
    }

    private static void WriteCasBitmap(byte[] data, int offset, int byteCount, HashSet<int> cas)
    {
        for (int i = 0; i < byteCount; i++)
            data[offset + i] = 0;
        foreach (int cl in cas)
        {
            if (cl < 20 || cl > 98 || cl % 2 != 0) continue;
            int bit = (cl - 20) / 2;
            if (bit >= byteCount * 8) continue;
            data[offset + bit / 8] |= (byte)(1 << (bit % 8));
        }
    }

    private static int ClampU16(int v) => Math.Clamp(v, 0, 0xFFFF);
    private static byte ClampU8(int v) => (byte)Math.Clamp(v, 0, 255);

    private static Ddr5ProfileView LoadJedec(byte[] data)
    {
        // 主时序至少到 tRFCsb_slr @ 46；次要时序到 tRTP limit @ 93
        if (data.Length < 48)
            return new Ddr5ProfileView { TabTitle = "SPD", Kind = Ddr5ProfileKind.Jedec, Present = false };

        int tck = SpdUtils.GetWord(data, 20);
        bool hasSec = data.Length >= 94;
        return new Ddr5ProfileView
        {
            TabTitle = "SPD",
            Kind = Ddr5ProfileKind.Jedec,
            BaseOffset = 0,
            Writable = data.Length >= 94,
            Present = tck > 0,
            Enabled = true,
            ProfileName = "JEDEC",
            TckPs = tck,
            SupportedCas = ReadCasBitmap(data, 24, 5),
            TclPs = SpdUtils.GetWord(data, 30),
            TrcdPs = SpdUtils.GetWord(data, 32),
            TrpPs = SpdUtils.GetWord(data, 34),
            TrasPs = SpdUtils.GetWord(data, 36),
            TrcPs = SpdUtils.GetWord(data, 38),
            TwrPs = SpdUtils.GetWord(data, 40),
            Trfc1Ns = SpdUtils.GetWord(data, 42),
            Trfc2Ns = SpdUtils.GetWord(data, 44),
            TrfcsbNs = SpdUtils.GetWord(data, 46),
            TrrdlPs = hasSec ? SpdUtils.GetWord(data, 70) : 0,
            TrrdlMin = hasSec ? data[72] : 0,
            TccdLPs = hasSec ? SpdUtils.GetWord(data, 73) : 0,
            TccdLMin = hasSec ? data[75] : 0,
            TccdLWrPs = hasSec ? SpdUtils.GetWord(data, 76) : 0,
            TccdLWrMin = hasSec ? data[78] : 0,
            TccdLWr2Ps = hasSec ? SpdUtils.GetWord(data, 79) : 0,
            TccdLWr2Min = hasSec ? data[81] : 0,
            TfawPs = hasSec ? SpdUtils.GetWord(data, 82) : 0,
            TfawMin = hasSec ? data[84] : 0,
            TccdLWtrPs = hasSec ? SpdUtils.GetWord(data, 85) : 0,
            TccdLWtrMin = hasSec ? data[87] : 0,
            TccdSWtrPs = hasSec ? SpdUtils.GetWord(data, 88) : 0,
            TccdSWtrMin = hasSec ? data[90] : 0,
            TrtpPs = hasSec ? SpdUtils.GetWord(data, 91) : 0,
            TrtpMin = hasSec ? data[93] : 0,
        };
    }

    private static Ddr5ProfileView LoadXmpProfile(
        byte[] data, int baseOff, string title, string name, bool enabled, bool regionPresent)
    {
        if (!regionPresent || data.Length < baseOff + 64)
            return new Ddr5ProfileView { TabTitle = title, Kind = Ddr5ProfileKind.Xmp, BaseOffset = baseOff, Present = false, ProfileName = name };

        int tck = SpdUtils.GetWord(data, baseOff + 5);
        int trrdl = SpdUtils.GetWord(data, baseOff + 31);
        int trrdlMin = data[baseOff + 33];
        int tccdLWr = SpdUtils.GetWord(data, baseOff + 34);
        int tccdLWrMin = data[baseOff + 36];
        int tccdLWr2 = SpdUtils.GetWord(data, baseOff + 37);
        int tccdLWr2Min = data[baseOff + 39];
        int tccdLWtr = SpdUtils.GetWord(data, baseOff + 40);
        int tccdLWtrMin = data[baseOff + 42];
        int tccdSWtr = SpdUtils.GetWord(data, baseOff + 43);
        int tccdSWtrMin = data[baseOff + 45];
        int tccdL = SpdUtils.GetWord(data, baseOff + 46);
        int tccdLMin = data[baseOff + 48];
        int trtp = SpdUtils.GetWord(data, baseOff + 49);
        int trtpMin = data[baseOff + 51];
        int tfaw = SpdUtils.GetWord(data, baseOff + 52);
        int tfawMin = data[baseOff + 54];

        // 部分厂条 XMP 只写主时序，次要时序为 0 —— 从 JEDEC SPD 回填（与参考编辑器一致）
        if (IsSecondaryEmpty(trrdl, tccdL, tccdLWr, tccdLWr2, tfaw, tccdLWtr, tccdSWtr, trtp)
            && data.Length >= 94)
        {
            trrdl = SpdUtils.GetWord(data, 70);
            trrdlMin = data[72];
            tccdL = SpdUtils.GetWord(data, 73);
            tccdLMin = data[75];
            tccdLWr = SpdUtils.GetWord(data, 76);
            tccdLWrMin = data[78];
            tccdLWr2 = SpdUtils.GetWord(data, 79);
            tccdLWr2Min = data[81];
            tfaw = SpdUtils.GetWord(data, 82);
            tfawMin = data[84];
            tccdLWtr = SpdUtils.GetWord(data, 85);
            tccdLWtrMin = data[87];
            tccdSWtr = SpdUtils.GetWord(data, 88);
            tccdSWtrMin = data[90];
            trtp = SpdUtils.GetWord(data, 91);
            trtpMin = data[93];
        }

        return new Ddr5ProfileView
        {
            TabTitle = title,
            Kind = Ddr5ProfileKind.Xmp,
            BaseOffset = baseOff,
            Writable = true,
            Present = tck > 0 || enabled,
            Enabled = enabled,
            ProfileName = name,
            TckPs = tck,
            VppRaw = data[baseOff],
            VddRaw = data[baseOff + 1],
            VddqRaw = data[baseOff + 2],
            VmemRaw = data[baseOff + 4],
            SupportedCas = ReadCasBitmap(data, baseOff + 7, 5),
            TclPs = SpdUtils.GetWord(data, baseOff + 13),
            TrcdPs = SpdUtils.GetWord(data, baseOff + 15),
            TrpPs = SpdUtils.GetWord(data, baseOff + 17),
            TrasPs = SpdUtils.GetWord(data, baseOff + 19),
            TrcPs = SpdUtils.GetWord(data, baseOff + 21),
            TwrPs = SpdUtils.GetWord(data, baseOff + 23),
            Trfc1Ns = SpdUtils.GetWord(data, baseOff + 25),
            Trfc2Ns = SpdUtils.GetWord(data, baseOff + 27),
            TrfcsbNs = SpdUtils.GetWord(data, baseOff + 29),
            TrrdlPs = trrdl,
            TrrdlMin = trrdlMin,
            TccdLWrPs = tccdLWr,
            TccdLWrMin = tccdLWrMin,
            TccdLWr2Ps = tccdLWr2,
            TccdLWr2Min = tccdLWr2Min,
            TccdLWtrPs = tccdLWtr,
            TccdLWtrMin = tccdLWtrMin,
            TccdSWtrPs = tccdSWtr,
            TccdSWtrMin = tccdSWtrMin,
            TccdLPs = tccdL,
            TccdLMin = tccdLMin,
            TrtpPs = trtp,
            TrtpMin = trtpMin,
            TfawPs = tfaw,
            TfawMin = tfawMin,
            IntelBoost = (data[baseOff + 59] & 1) != 0,
            RealTimeOc = (data[baseOff + 59] & 2) != 0,
            CommandRate = data[baseOff + 60] & 0x0F,
        };
    }

    private static bool IsSecondaryEmpty(
        int trrdl, int tccdL, int tccdLWr, int tccdLWr2,
        int tfaw, int tccdLWtr, int tccdSWtr, int trtp) =>
        trrdl == 0 && tccdL == 0 && tccdLWr == 0 && tccdLWr2 == 0
        && tfaw == 0 && tccdLWtr == 0 && tccdSWtr == 0 && trtp == 0;

    private static Ddr5ProfileView LoadExpoProfile(byte[] data, int baseOff, string title, bool enabled)
    {
        if (data.Length < baseOff + 40)
            return new Ddr5ProfileView { TabTitle = title, Kind = Ddr5ProfileKind.Expo, BaseOffset = baseOff, Present = false };

        int tck = SpdUtils.GetWord(data, baseOff + 4);
        int trrdl = SpdUtils.GetWord(data, baseOff + 24);
        int tccdL = SpdUtils.GetWord(data, baseOff + 26);
        int tccdLWr = SpdUtils.GetWord(data, baseOff + 28);
        int tccdLWr2 = SpdUtils.GetWord(data, baseOff + 30);
        int tfaw = SpdUtils.GetWord(data, baseOff + 32);
        int tccdLWtr = SpdUtils.GetWord(data, baseOff + 34);
        int tccdSWtr = SpdUtils.GetWord(data, baseOff + 36);
        int trtp = SpdUtils.GetWord(data, baseOff + 38);

        int trrdlMin = 0, tccdLMin = 0, tccdLWrMin = 0, tccdLWr2Min = 0;
        int tfawMin = 0, tccdLWtrMin = 0, tccdSWtrMin = 0, trtpMin = 0;

        if (IsSecondaryEmpty(trrdl, tccdL, tccdLWr, tccdLWr2, tfaw, tccdLWtr, tccdSWtr, trtp)
            && data.Length >= 94)
        {
            trrdl = SpdUtils.GetWord(data, 70);
            trrdlMin = data[72];
            tccdL = SpdUtils.GetWord(data, 73);
            tccdLMin = data[75];
            tccdLWr = SpdUtils.GetWord(data, 76);
            tccdLWrMin = data[78];
            tccdLWr2 = SpdUtils.GetWord(data, 79);
            tccdLWr2Min = data[81];
            tfaw = SpdUtils.GetWord(data, 82);
            tfawMin = data[84];
            tccdLWtr = SpdUtils.GetWord(data, 85);
            tccdLWtrMin = data[87];
            tccdSWtr = SpdUtils.GetWord(data, 88);
            tccdSWtrMin = data[90];
            trtp = SpdUtils.GetWord(data, 91);
            trtpMin = data[93];
        }

        return new Ddr5ProfileView
        {
            TabTitle = title,
            Kind = Ddr5ProfileKind.Expo,
            BaseOffset = baseOff,
            Writable = true,
            Present = tck > 0 || enabled,
            Enabled = enabled,
            TckPs = tck,
            VddRaw = data[baseOff],
            VddqRaw = data[baseOff + 1],
            VppRaw = data[baseOff + 2],
            TclPs = SpdUtils.GetWord(data, baseOff + 6),
            TrcdPs = SpdUtils.GetWord(data, baseOff + 8),
            TrpPs = SpdUtils.GetWord(data, baseOff + 10),
            TrasPs = SpdUtils.GetWord(data, baseOff + 12),
            TrcPs = SpdUtils.GetWord(data, baseOff + 14),
            TwrPs = SpdUtils.GetWord(data, baseOff + 16),
            Trfc1Ns = SpdUtils.GetWord(data, baseOff + 18),
            Trfc2Ns = SpdUtils.GetWord(data, baseOff + 20),
            TrfcsbNs = SpdUtils.GetWord(data, baseOff + 22),
            TrrdlPs = trrdl,
            TrrdlMin = trrdlMin,
            TccdLPs = tccdL,
            TccdLMin = tccdLMin,
            TccdLWrPs = tccdLWr,
            TccdLWrMin = tccdLWrMin,
            TccdLWr2Ps = tccdLWr2,
            TccdLWr2Min = tccdLWr2Min,
            TfawPs = tfaw,
            TfawMin = tfawMin,
            TccdLWtrPs = tccdLWtr,
            TccdLWtrMin = tccdLWtrMin,
            TccdSWtrPs = tccdSWtr,
            TccdSWtrMin = tccdSWtrMin,
            TrtpPs = trtp,
            TrtpMin = trtpMin,
        };
    }

    private static HashSet<int> ReadCasBitmap(byte[] data, int offset, int byteCount)
    {
        var set = new HashSet<int>();
        if (data.Length < offset + byteCount) return set;
        for (int bit = 0; bit < byteCount * 8; bit++)
        {
            int cl = 20 + bit * 2;
            if (cl > 98) break;
            if ((data[offset + bit / 8] & (1 << (bit % 8))) != 0)
                set.Add(cl);
        }
        return set;
    }

    private static string ReadName(byte[] data, int offset)
    {
        if (data.Length < offset + 16) return "";
        var chars = new char[16];
        int n = 0;
        for (int i = 0; i < 16; i++)
        {
            byte b = data[offset + i];
            if (b == 0) break;
            chars[n++] = b is >= 0x20 and <= 0x7E ? (char)b : ' ';
        }
        return new string(chars, 0, n).Trim();
    }
}

namespace SpdEditor.Core;

public enum Ddr4ProfileKind
{
    Jedec,
    Xmp,
}

/// <summary>DDR4 配置页展示模型（JEDEC SPD / XMP 2.0）。</summary>
public sealed class Ddr4ProfileView
{
    public string TabTitle { get; init; } = "";
    public Ddr4ProfileKind Kind { get; init; }
    public int BaseOffset { get; init; } = -1;
    public bool Writable { get; init; }
    public bool Present { get; init; }
    public bool Enabled { get; init; }

    /// <summary>最小周期 MTB ticks（写回用）。</summary>
    public int TckMtb { get; init; }
    /// <summary>最小周期 FTB（signed，单位 ps）。</summary>
    public int TckFc { get; init; }
    /// <summary>带 FTB 的最小周期（ps），用于频率标签。</summary>
    public int TckPs => SpdUtils.TicksToPsDdr4(TckMtb) + TckFc;
    public int ClockMhz => TckPs > 0 ? (int)Math.Round(1_000_000.0 / TckPs) : 0;
    public int MtPerSec => TckPs > 0 ? (int)Math.Round(SpdUtils.MtFromMinCyclePs(TckPs)) : 0;

    /// <summary>VDD 厘伏（120 = 1.20 V）；JEDEC 页为 0。</summary>
    public int VddCv { get; init; }

    public HashSet<int> SupportedCas { get; init; } = [];

    public int ClTicks { get; init; }
    public int ClFc { get; init; }
    public int RcdTicks { get; init; }
    public int RcdFc { get; init; }
    public int RpTicks { get; init; }
    public int RpFc { get; init; }
    public int RasTicks { get; init; }
    public int RcTicks { get; init; }
    public int RcFc { get; init; }
    public int WrTicks { get; init; }
    public int Rfc1Ticks { get; init; }
    public int Rfc2Ticks { get; init; }
    public int Rfc4Ticks { get; init; }
    public int FawTicks { get; init; }
    public int RrdsTicks { get; init; }
    public int RrdsFc { get; init; }
    public int RrdlTicks { get; init; }
    public int RrdlFc { get; init; }
    public int CcdlTicks { get; init; }
    public int CcdlFc { get; init; }
    public int WtrsTicks { get; init; }
    public int WtrlTicks { get; init; }

    public double TicksToNs(int ticks, int fc = 0) =>
        ticks * SpdUtils.Ddr4MtbNs + fc * 0.001;
}

/// <summary>窗体收集后的可写回字段（以 MTB ticks 为主）。</summary>
public sealed class Ddr4ProfileEdit
{
    public int TckMtb { get; set; }
    public int TckFc { get; set; }
    public int TckPs { get; set; }
    public HashSet<int> SupportedCas { get; set; } = [];
    public int VddCv { get; set; }

    public int ClTicks { get; set; }
    public int ClFc { get; set; }
    public int RcdTicks { get; set; }
    public int RcdFc { get; set; }
    public int RpTicks { get; set; }
    public int RpFc { get; set; }
    public int RasTicks { get; set; }
    public int RcTicks { get; set; }
    public int RcFc { get; set; }
    public int WrTicks { get; set; }
    public int Rfc1Ticks { get; set; }
    public int Rfc2Ticks { get; set; }
    public int Rfc4Ticks { get; set; }
    public int FawTicks { get; set; }
    public int RrdsTicks { get; set; }
    public int RrdsFc { get; set; }
    public int RrdlTicks { get; set; }
    public int RrdlFc { get; set; }
    public int CcdlTicks { get; set; }
    public int CcdlFc { get; set; }
    public int WtrsTicks { get; set; }
    public int WtrlTicks { get; set; }
}

/// <summary>从 DDR4 SPD 映像加载/写回 JEDEC 与 XMP 2.0 配置页。</summary>
public static class Ddr4ProfileLoader
{
    public const int XmpOffset = 0x180;
    public const int XmpHeaderSize = 9;
    public const int XmpProfileOffset = 0x189;
    public const int XmpProfileSize = 0x2F;
    public const int XmpP1 = 0x189;
    public const int XmpP2 = 0x1B8;

    public static bool HasXmp(byte[] data) =>
        data.Length >= XmpOffset + 2 && data[XmpOffset] == 0x0C && data[XmpOffset + 1] == 0x4A;

    public static IReadOnlyList<Ddr4ProfileView> LoadAll(byte[] data)
    {
        var list = new List<Ddr4ProfileView> { LoadJedec(data) };
        bool xmp = HasXmp(data);
        byte enable = xmp && data.Length > XmpOffset + 2 ? data[XmpOffset + 2] : (byte)0;
        list.Add(LoadXmpProfile(data, XmpP1, "XMP 1", (enable & 1) != 0, xmp));
        list.Add(LoadXmpProfile(data, XmpP2, "XMP 2", (enable & 2) != 0, xmp));
        return list;
    }

    public static void SetXmpEnableBits(byte[] data, bool p1, bool p2)
    {
        EnsureXmpHeader(data);
        if (data.Length <= XmpOffset + 2) return;
        byte v = data[XmpOffset + 2];
        v = (byte)((v & ~0x03) | (p1 ? 1 : 0) | (p2 ? 2 : 0));
        data[XmpOffset + 2] = v;
    }

    public static bool TryWriteProfile(byte[] data, Ddr4ProfileView meta, Ddr4ProfileEdit edit, out string error)
    {
        error = "";
        if (!meta.Writable || meta.BaseOffset < 0)
        {
            error = $"{meta.TabTitle} 不可写回";
            return false;
        }

        SpdTimingRules.NormalizeDdr4(edit, meta.Kind);

        return meta.Kind switch
        {
            Ddr4ProfileKind.Jedec => WriteJedec(data, edit, out error),
            Ddr4ProfileKind.Xmp => WriteXmp(data, meta.BaseOffset, edit, out error),
            _ => false,
        };
    }

    private static void EnsureXmpHeader(byte[] data)
    {
        if (data.Length < XmpOffset + XmpHeaderSize) return;
        data[XmpOffset] = 0x0C;
        data[XmpOffset + 1] = 0x4A;
        if (data[XmpOffset + 3] == 0)
            data[XmpOffset + 3] = 0x20;
    }

    private static bool WriteJedec(byte[] data, Ddr4ProfileEdit e, out string error)
    {
        error = "";
        if (data.Length < 126) { error = "SPD 长度不足"; return false; }

        int tckMtb = e.TckMtb;
        int tckFc = e.TckFc;
        if (e.TckPs > 0 && tckMtb == 0)
            (tckMtb, tckFc) = SpdUtils.PsToTicksFcDdr4(e.TckPs);

        data[18] = ClampU8(tckMtb);
        data[125] = (byte)tckFc;
        WriteCasBitmap(data, 20, 4, e.SupportedCas, maxCl: 36);

        data[24] = ClampU8(e.ClTicks);
        data[123] = (byte)e.ClFc;
        data[25] = ClampU8(e.RcdTicks);
        data[122] = (byte)e.RcdFc;
        data[26] = ClampU8(e.RpTicks);
        data[121] = (byte)e.RpFc;
        WriteRasRc(data, 27, 28, 29, e.RasTicks, e.RcTicks);
        data[120] = (byte)e.RcFc;
        SpdUtils.SetWord(data, 30, ClampU16(e.Rfc1Ticks));
        SpdUtils.SetWord(data, 32, ClampU16(e.Rfc2Ticks));
        SpdUtils.SetWord(data, 34, ClampU16(e.Rfc4Ticks));
        WriteFaw(data, 36, 37, e.FawTicks);
        data[38] = ClampU8(e.RrdsTicks);
        data[119] = (byte)e.RrdsFc;
        data[39] = ClampU8(e.RrdlTicks);
        data[118] = (byte)e.RrdlFc;
        data[40] = ClampU8(e.CcdlTicks);
        data[117] = (byte)e.CcdlFc;
        WriteWr(data, 41, 42, e.WrTicks);
        WriteWtr(data, 43, 44, 45, e.WtrsTicks, e.WtrlTicks);
        return true;
    }

    private static bool WriteXmp(byte[] data, int baseOff, Ddr4ProfileEdit e, out string error)
    {
        error = "";
        if (data.Length < baseOff + XmpProfileSize) { error = "XMP 块长度不足"; return false; }
        EnsureXmpHeader(data);

        int tckMtb = e.TckMtb;
        int tckFc = e.TckFc;
        if (e.TckPs > 0 && (tckMtb == 0 || Math.Abs(SpdUtils.TicksToPsDdr4(tckMtb) + tckFc - e.TckPs) > 1))
            (tckMtb, tckFc) = SpdUtils.PsToTicksFcDdr4(e.TckPs);

        data[baseOff] = SpdUtils.Ddr4VoltageCvToByte(e.VddCv > 0 ? e.VddCv : 120);
        data[baseOff + 3] = ClampU8(tckMtb);
        WriteCasBitmap(data, baseOff + 4, 3, e.SupportedCas, maxCl: 30);
        data[baseOff + 8] = ClampU8(e.ClTicks);
        data[baseOff + 9] = ClampU8(e.RcdTicks);
        data[baseOff + 10] = ClampU8(e.RpTicks);
        WriteRasRc(data, baseOff + 11, baseOff + 12, baseOff + 13, e.RasTicks, e.RcTicks);
        SpdUtils.SetWord(data, baseOff + 14, ClampU16(e.Rfc1Ticks));
        SpdUtils.SetWord(data, baseOff + 16, ClampU16(e.Rfc2Ticks));
        SpdUtils.SetWord(data, baseOff + 18, ClampU16(e.Rfc4Ticks));
        WriteFaw(data, baseOff + 20, baseOff + 21, e.FawTicks);
        data[baseOff + 22] = ClampU8(e.RrdsTicks);
        data[baseOff + 23] = ClampU8(e.RrdlTicks);
        data[baseOff + 32] = (byte)e.RrdlFc;
        data[baseOff + 33] = (byte)e.RrdsFc;
        data[baseOff + 34] = (byte)e.RcFc;
        data[baseOff + 35] = (byte)e.RpFc;
        data[baseOff + 36] = (byte)e.RcdFc;
        data[baseOff + 37] = (byte)e.ClFc;
        data[baseOff + 38] = (byte)tckFc;
        return true;
    }

    private static void WriteCasBitmap(byte[] data, int offset, int byteCount, HashSet<int> cas, int maxCl)
    {
        for (int i = 0; i < byteCount; i++)
            data[offset + i] = 0;
        foreach (int cl in cas)
        {
            if (cl < 7 || cl > maxCl) continue;
            int bit = cl - 7;
            if (bit >= byteCount * 8) continue;
            data[offset + bit / 8] |= (byte)(1 << (bit % 8));
        }
    }

    private static void WriteRasRc(byte[] data, int upperOff, int rasOff, int rcOff, int ras, int rc)
    {
        data[upperOff] = (byte)(((rc >> 8) & 0x0F) << 4 | ((ras >> 8) & 0x0F));
        data[rasOff] = (byte)(ras & 0xFF);
        data[rcOff] = (byte)(rc & 0xFF);
    }

    private static void WriteFaw(byte[] data, int upperOff, int lowOff, int faw)
    {
        data[upperOff] = (byte)((data[upperOff] & 0xF0) | ((faw >> 8) & 0x0F));
        data[lowOff] = (byte)(faw & 0xFF);
    }

    private static void WriteWr(byte[] data, int upperOff, int lowOff, int wr)
    {
        data[upperOff] = (byte)((data[upperOff] & 0xF0) | ((wr >> 8) & 0x0F));
        data[lowOff] = (byte)(wr & 0xFF);
    }

    private static void WriteWtr(byte[] data, int upperOff, int wtrsOff, int wtrlOff, int wtrs, int wtrl)
    {
        data[upperOff] = (byte)((((wtrl >> 8) & 0x0F) << 4) | ((wtrs >> 8) & 0x0F));
        data[wtrsOff] = (byte)(wtrs & 0xFF);
        data[wtrlOff] = (byte)(wtrl & 0xFF);
    }

    private static int ClampU16(int v) => Math.Clamp(v, 0, 0xFFFF);
    private static byte ClampU8(int v) => (byte)Math.Clamp(v, 0, 255);

    private static Ddr4ProfileView LoadJedec(byte[] data)
    {
        if (data.Length < 46)
            return new Ddr4ProfileView { TabTitle = "SPD", Kind = Ddr4ProfileKind.Jedec, Present = false };

        bool hasFc = data.Length > 125;
        int tckMtb = data[18];
        int tckFc = hasFc ? SpdUtils.SignedByte(data[125]) : 0;
        int ras = ((data[27] & 0x0F) << 8) | data[28];
        int rc = ((data[27] >> 4) & 0x0F) << 8 | data[29];
        int faw = ((data[36] & 0x0F) << 8) | data[37];
        int wr = data.Length > 42 ? ((data[41] & 0x0F) << 8) | data[42] : 0;
        int wtrs = data.Length > 45 ? ((data[43] & 0x0F) << 8) | data[44] : 0;
        int wtrl = data.Length > 45 ? (((data[43] >> 4) & 0x0F) << 8) | data[45] : 0;

        return new Ddr4ProfileView
        {
            TabTitle = "SPD",
            Kind = Ddr4ProfileKind.Jedec,
            BaseOffset = 0,
            Writable = data.Length >= 126,
            Present = tckMtb > 0,
            Enabled = true,
            TckMtb = tckMtb,
            TckFc = tckFc,
            SupportedCas = ReadCasBitmap(data, 20, 4, maxCl: 36),
            ClTicks = data[24],
            ClFc = hasFc ? SpdUtils.SignedByte(data[123]) : 0,
            RcdTicks = data[25],
            RcdFc = hasFc ? SpdUtils.SignedByte(data[122]) : 0,
            RpTicks = data[26],
            RpFc = hasFc ? SpdUtils.SignedByte(data[121]) : 0,
            RasTicks = ras,
            RcTicks = rc,
            RcFc = hasFc ? SpdUtils.SignedByte(data[120]) : 0,
            WrTicks = wr,
            Rfc1Ticks = SpdUtils.GetWord(data, 30),
            Rfc2Ticks = SpdUtils.GetWord(data, 32),
            Rfc4Ticks = SpdUtils.GetWord(data, 34),
            FawTicks = faw,
            RrdsTicks = data[38],
            RrdsFc = hasFc ? SpdUtils.SignedByte(data[119]) : 0,
            RrdlTicks = data[39],
            RrdlFc = hasFc ? SpdUtils.SignedByte(data[118]) : 0,
            CcdlTicks = data[40],
            CcdlFc = hasFc ? SpdUtils.SignedByte(data[117]) : 0,
            WtrsTicks = wtrs,
            WtrlTicks = wtrl,
        };
    }

    private static Ddr4ProfileView LoadXmpProfile(
        byte[] data, int baseOff, string title, bool enabled, bool regionPresent)
    {
        if (!regionPresent || data.Length < baseOff + XmpProfileSize)
        {
            return new Ddr4ProfileView
            {
                TabTitle = title,
                Kind = Ddr4ProfileKind.Xmp,
                BaseOffset = baseOff,
                Present = false,
            };
        }

        int tckMtb = data[baseOff + 3];
        int tckFc = SpdUtils.SignedByte(data[baseOff + 38]);
        int ras = ((data[baseOff + 11] & 0x0F) << 8) | data[baseOff + 12];
        int rc = (((data[baseOff + 11] >> 4) & 0x0F) << 8) | data[baseOff + 13];
        int faw = ((data[baseOff + 20] & 0x0F) << 8) | data[baseOff + 21];
        int vddCv = SpdUtils.Ddr4VoltageByteToCv(data[baseOff]);
        if (vddCv == 0) vddCv = 120;

        return new Ddr4ProfileView
        {
            TabTitle = title,
            Kind = Ddr4ProfileKind.Xmp,
            BaseOffset = baseOff,
            Writable = true,
            Present = tckMtb > 0 || enabled,
            Enabled = enabled,
            TckMtb = tckMtb,
            TckFc = tckFc,
            VddCv = vddCv,
            SupportedCas = ReadCasBitmap(data, baseOff + 4, 3, maxCl: 30),
            ClTicks = data[baseOff + 8],
            ClFc = SpdUtils.SignedByte(data[baseOff + 37]),
            RcdTicks = data[baseOff + 9],
            RcdFc = SpdUtils.SignedByte(data[baseOff + 36]),
            RpTicks = data[baseOff + 10],
            RpFc = SpdUtils.SignedByte(data[baseOff + 35]),
            RasTicks = ras,
            RcTicks = rc,
            RcFc = SpdUtils.SignedByte(data[baseOff + 34]),
            WrTicks = SpdUtils.PsToMtbTicksDdr4(15_000),
            Rfc1Ticks = SpdUtils.GetWord(data, baseOff + 14),
            Rfc2Ticks = SpdUtils.GetWord(data, baseOff + 16),
            Rfc4Ticks = SpdUtils.GetWord(data, baseOff + 18),
            FawTicks = faw,
            RrdsTicks = data[baseOff + 22],
            RrdsFc = SpdUtils.SignedByte(data[baseOff + 33]),
            RrdlTicks = data[baseOff + 23],
            RrdlFc = SpdUtils.SignedByte(data[baseOff + 32]),
        };
    }

    private static HashSet<int> ReadCasBitmap(byte[] data, int offset, int byteCount, int maxCl)
    {
        var set = new HashSet<int>();
        if (data.Length < offset + byteCount) return set;
        for (int bit = 0; bit < byteCount * 8; bit++)
        {
            int cl = 7 + bit;
            if (cl > maxCl) break;
            if ((data[offset + bit / 8] & (1 << (bit % 8))) != 0)
                set.Add(cl);
        }
        return set;
    }
}

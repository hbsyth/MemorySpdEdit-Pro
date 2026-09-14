using System.Text.Json;

namespace SpdEditor.Core.Xmp;

public sealed class SpeedBinEntry
{
    public int MtS { get; set; }
    public double TCKminNs { get; set; }
    public double TCKmaxNs { get; set; }
    public int CL { get; set; }
    public List<int> SupportedCl { get; set; } = [];
    public double TAAns { get; set; }
    public double TRCDns { get; set; }
    public double TRPNs { get; set; }
    public double TRASns { get; set; }
    public double TRCns { get; set; }
    public double TWRns { get; set; }
    public double TWTR_Sns { get; set; }
    public double TWTR_Lns { get; set; }
    public double TRRD_Sns { get; set; }
    public double TRRD_Lns { get; set; }
    public double TRFC1ns { get; set; }
    public double TRFC2ns { get; set; }
    public double TRFC4ns { get; set; }
    public double TFAWns { get; set; }
}

public static class SpeedBinService
{
    private static Dictionary<string, SpeedBinEntry>? _ddr4;
    private static Dictionary<string, SpeedBinEntry>? _ddr5;

    public static IReadOnlyList<string> GetBinNames(bool ddr5)
    {
        EnsureLoaded();
        var dict = ddr5 ? _ddr5! : _ddr4!;
        return dict.Keys.OrderBy(k => k).ToList();
    }

    public static bool TryGet(bool ddr5, string name, out SpeedBinEntry entry)
    {
        EnsureLoaded();
        var dict = ddr5 ? _ddr5! : _ddr4!;
        return dict.TryGetValue(name, out entry!);
    }

    public static void ApplyToDdr4Jedec(Ddr4AdvancedModel spd, string binName)
    {
        if (!TryGet(false, binName, out var b)) return;
        spd.MinCycleTicks = SpdUtils.NsToTicksDdr4(b.TCKminNs);
        spd.MinCycleFc = 0;
        foreach (int cl in Enumerable.Range(7, 30))
            spd.SetClSupported(cl, false);
        foreach (int cl in GetSupportedCl(b).Where(c => c is >= 7 and <= 36))
            spd.SetClSupported(cl, true);
        spd.ClTicks = SpdUtils.NsToTicksDdr4(b.TAAns);
        spd.RcdTicks = SpdUtils.NsToTicksDdr4(b.TRCDns);
        spd.RpTicks = SpdUtils.NsToTicksDdr4(b.TRPNs);
        spd.RasTicks = SpdUtils.NsToTicksDdr4(b.TRASns);
        spd.RcTicks = SpdUtils.NsToTicksDdr4(b.TRCns);
        spd.WrTicks = SpdUtils.NsToTicksDdr4(b.TWRns);
        spd.Rfc1Ticks = (int)Math.Round(b.TRFC1ns / SpdUtils.Ddr4MtbNs);
        spd.Rfc2Ticks = (int)Math.Round(b.TRFC2ns / SpdUtils.Ddr4MtbNs);
        spd.Rfc4Ticks = (int)Math.Round(b.TRFC4ns / SpdUtils.Ddr4MtbNs);
        spd.RrdsTicks = SpdUtils.NsToTicksDdr4(b.TRRD_Sns);
        spd.RrdlTicks = SpdUtils.NsToTicksDdr4(b.TRRD_Lns);
        spd.FawTicks = SpdUtils.NsToTicksDdr4(b.TFAWns);
        spd.WtrsTicks = SpdUtils.NsToTicksDdr4(b.TWTR_Sns);
        spd.WtrlTicks = SpdUtils.NsToTicksDdr4(b.TWTR_Lns);
    }

    public static void ApplyToDdr5Jedec(Ddr5AdvancedModel spd, string binName)
    {
        if (!TryGet(true, binName, out var b)) return;
        static int Ns2Ps(double ns) => (int)Math.Round(ns * 1000);
        spd.MinCycleTime = Ns2Ps(b.TCKminNs);
        spd.MaxCycleTime = Ns2Ps(b.TCKmaxNs);
        int mct = spd.MinCycleTime;
        for (int cl = 20; cl <= 98; cl += 2)
            spd.SetClSupported(cl, false);
        foreach (int cl in GetSupportedCl(b))
            spd.SetClSupported(cl, true);
        spd.TAA = Ns2Ps(b.TAAns);
        spd.TRCD = Ns2Ps(b.TRCDns);
        spd.TRP = Ns2Ps(b.TRPNs);
        spd.TRAS = Ns2Ps(b.TRASns);
        spd.TRC = Ns2Ps(b.TRCns);
        spd.TWR = Ns2Ps(b.TWRns);
        spd.TRFC1Slr = (int)Math.Round(b.TRFC1ns);
        spd.TRFC2Slr = (int)Math.Round(b.TRFC2ns);
        spd.TRFCsbSlr = (int)Math.Round(b.TRFC4ns);
        spd.TRRD_L = Ns2Ps(b.TRRD_Lns);
        spd.TCCD_L = Ns2Ps(Math.Max(b.TRRD_Lns, 4.0));
        spd.TCCD_L_WR = Math.Max(32 * mct, 20000);
        spd.TCCD_L_WR2 = Math.Max(16 * mct, 10000);
        spd.TFAW = Ns2Ps(b.TFAWns);
        spd.TCCD_L_WTR = Ns2Ps(b.TWTR_Lns);
        spd.TCCD_S_WTR = Ns2Ps(b.TWTR_Sns);
        spd.TRTP = Ns2Ps(Math.Max(b.TWTR_Lns, 7.5));
        spd.TRRD_L_LowerLimit = Math.Max(4, SpdUtils.TimeToTicksDdr5(Ns2Ps(b.TRRD_Lns), mct));
        spd.TCCD_L_LowerLimit = Math.Max(4, SpdUtils.TimeToTicksDdr5(Ns2Ps(Math.Max(b.TRRD_Lns, 4.0)), mct));
        spd.TFAW_LowerLimit = Math.Max(20, SpdUtils.TimeToTicksDdr5(Ns2Ps(b.TFAWns), mct));
    }

    public static void ApplyToDdr4Xmp(Ddr4XmpProfile xmp, string binName)
    {
        if (!TryGet(false, binName, out var b)) return;
        xmp.MinCycleTimePs = (int)Math.Round(b.TCKminNs * 1000);
        for (int cl = 7; cl <= 36; cl++) xmp.SetClSupported(cl, false);
        foreach (int cl in GetSupportedCl(b).Where(c => c is >= 7 and <= 36))
            xmp.SetClSupported(cl, true);
        xmp.VddCentivolts = 120;
        xmp.ClTicks = SpdUtils.NsToTicksDdr4(b.TAAns);
        xmp.RcdTicks = SpdUtils.NsToTicksDdr4(b.TRCDns);
        xmp.RpTicks = SpdUtils.NsToTicksDdr4(b.TRPNs);
        xmp.RasTicks = SpdUtils.NsToTicksDdr4(b.TRASns);
        xmp.RcTicks = SpdUtils.NsToTicksDdr4(b.TRCns);
        xmp.WrTicks = SpdUtils.NsToTicksDdr4(b.TWRns);
        xmp.Rfc1Ticks = (int)Math.Round(b.TRFC1ns / SpdUtils.Ddr4MtbNs);
        xmp.Rfc2Ticks = (int)Math.Round(b.TRFC2ns / SpdUtils.Ddr4MtbNs);
        xmp.Rfc4Ticks = (int)Math.Round(b.TRFC4ns / SpdUtils.Ddr4MtbNs);
        xmp.RrdsTicks = SpdUtils.NsToTicksDdr4(b.TRRD_Sns);
        xmp.RrdlTicks = SpdUtils.NsToTicksDdr4(b.TRRD_Lns);
        xmp.FawTicks = SpdUtils.NsToTicksDdr4(b.TFAWns);
    }

    public static void ApplyToDdr5Xmp(Ddr5XmpProfile xmp, string binName)
    {
        if (!TryGet(true, binName, out var b)) return;
        static int Ns2Ps(double ns) => (int)Math.Round(ns * 1000);
        xmp.MinCycleTime = Ns2Ps(b.TCKminNs);
        int mct = xmp.MinCycleTime;
        for (int cl = 20; cl <= 98; cl += 2)
            xmp.SetClSupported(cl, false);
        foreach (int cl in GetSupportedCl(b))
            xmp.SetClSupported(cl, true);
        xmp.Vdd = xmp.Vddq = xmp.Vmemctrl = 110;
        xmp.Vpp = 180;
        xmp.TAA = Ns2Ps(b.TAAns);
        xmp.TRCD = Ns2Ps(b.TRCDns);
        xmp.TRP = Ns2Ps(b.TRPNs);
        xmp.TRAS = Ns2Ps(b.TRASns);
        xmp.TRC = Ns2Ps(b.TRCns);
        xmp.TWR = Ns2Ps(b.TWRns);
        xmp.TRFC1 = (int)Math.Round(b.TRFC1ns);
        xmp.TRFC2 = (int)Math.Round(b.TRFC2ns);
        xmp.TRFC = (int)Math.Round(b.TRFC4ns);
        xmp.TRRD_L = Ns2Ps(b.TRRD_Lns);
        xmp.TCCD_L = Ns2Ps(Math.Max(b.TRRD_Lns, 4.0));
        xmp.TFAW = Ns2Ps(b.TFAWns);
        xmp.TCCD_L_WTR = Ns2Ps(b.TWTR_Lns);
        xmp.TCCD_S_WTR = Ns2Ps(b.TWTR_Sns);
        xmp.TRTP = Math.Max(Ns2Ps(b.TWTR_Lns), Ns2Ps(7.5));
        xmp.UpdateCrc();
    }

    public static void ApplyToExpo(ExpoProfile expo, string binName)
    {
        if (!TryGet(true, binName, out var b)) return;
        static int Ns2Ps(double ns) => (int)Math.Round(ns * 1000);
        expo.MinCycleTime = Ns2Ps(b.TCKminNs);
        expo.Vdd = expo.Vddq = 110;
        expo.Vpp = 180;
        expo.TAA = Ns2Ps(b.TAAns);
        expo.TRCD = Ns2Ps(b.TRCDns);
        expo.TRP = Ns2Ps(b.TRPNs);
        expo.TRAS = Ns2Ps(b.TRASns);
        expo.TRC = Ns2Ps(b.TRCns);
        expo.TWR = Ns2Ps(b.TWRns);
        expo.TRFC1 = (int)Math.Round(b.TRFC1ns);
        expo.TRFC2 = (int)Math.Round(b.TRFC2ns);
        expo.TRFC = (int)Math.Round(b.TRFC4ns);
        expo.TRRD_L = Ns2Ps(b.TRRD_Lns);
        expo.TCCD_L = Ns2Ps(Math.Max(b.TRRD_Lns, 4.0));
        expo.TFAW = Ns2Ps(b.TFAWns);
        expo.TCCD_L_WTR = Ns2Ps(b.TWTR_Lns);
        expo.TCCD_S_WTR = Ns2Ps(b.TWTR_Sns);
        expo.TRTP = Ns2Ps(Math.Max(b.TWTR_Lns, 7.5));
    }

    private static IEnumerable<int> GetSupportedCl(SpeedBinEntry b) =>
        b.SupportedCl.Count > 0 ? b.SupportedCl : [b.CL];

    private static void EnsureLoaded()
    {
        if (_ddr4 != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "Core", "Xmp", "speed_bins.json");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "speed_bins.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        _ddr4 = ParseBins(doc.RootElement.GetProperty("ddr4"));
        _ddr5 = ParseBins(doc.RootElement.GetProperty("ddr5"));
    }

    private static Dictionary<string, SpeedBinEntry> ParseBins(JsonElement el)
    {
        var dict = new Dictionary<string, SpeedBinEntry>();
        foreach (var prop in el.EnumerateObject())
        {
            var e = prop.Value.Deserialize<SpeedBinEntry>(new JsonSerializerOptions { PropertyNameCaseInsensitive = false })!;
            if (prop.Value.TryGetProperty("tAA_ns", out var taa)) e.TAAns = taa.GetDouble();
            if (prop.Value.TryGetProperty("tRCD_ns", out var v)) e.TRCDns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRP_ns", out v)) e.TRPNs = v.GetDouble();
            if (prop.Value.TryGetProperty("tRAS_ns", out v)) e.TRASns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRC_ns", out v)) e.TRCns = v.GetDouble();
            if (prop.Value.TryGetProperty("tWR_ns", out v)) e.TWRns = v.GetDouble();
            if (prop.Value.TryGetProperty("tWTR_S_ns", out v)) e.TWTR_Sns = v.GetDouble();
            if (prop.Value.TryGetProperty("tWTR_L_ns", out v)) e.TWTR_Lns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRRD_S_ns", out v)) e.TRRD_Sns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRRD_L_ns", out v)) e.TRRD_Lns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRFC1_ns", out v)) e.TRFC1ns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRFC2_ns", out v)) e.TRFC2ns = v.GetDouble();
            if (prop.Value.TryGetProperty("tRFC4_ns", out v)) e.TRFC4ns = v.GetDouble();
            if (prop.Value.TryGetProperty("tFAW_ns", out v)) e.TFAWns = v.GetDouble();
            if (prop.Value.TryGetProperty("tCKmin_ns", out v)) e.TCKminNs = v.GetDouble();
            if (prop.Value.TryGetProperty("tCKmax_ns", out v)) e.TCKmaxNs = v.GetDouble();
            if (prop.Value.TryGetProperty("mt_s", out var mt)) e.MtS = mt.GetInt32();
            if (prop.Value.TryGetProperty("CL", out var cl)) e.CL = cl.GetInt32();
            if (prop.Value.TryGetProperty("supported_cl", out var scl))
                e.SupportedCl = scl.EnumerateArray().Select(x => x.GetInt32()).ToList();
            dict[prop.Name] = e;
        }
        return dict;
    }
}

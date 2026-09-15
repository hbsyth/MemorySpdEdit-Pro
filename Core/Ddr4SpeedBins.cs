namespace SpdEditor.Core;

public sealed class Ddr4SpeedBin
{
    public required string Name { get; init; }
    public int MtS { get; init; }
    public double TckMinNs { get; init; }
    public double TckMaxNs { get; init; }
    public int Cl { get; init; }
    public int[] SupportedCl { get; init; } = [];
    public double TaaNs { get; init; }
    public double TrcdNs { get; init; }
    public double TrpNs { get; init; }
    public double TrasNs { get; init; }
    public double TrcNs { get; init; }
    public double TwrNs { get; init; }
    public double TwtrSNs { get; init; }
    public double TwtrLNs { get; init; }
    public double TrrdSNs { get; init; }
    public double TrrdLNs { get; init; }
    public double Trfc1Ns { get; init; }
    public double Trfc2Ns { get; init; }
    public double Trfc4Ns { get; init; }
    public double TfawNs { get; init; }
}

/// <summary>DDR4 JEDEC Speed Bins（JESD79-4D），对齐 ddrxmpeditor-pro。</summary>
public static class Ddr4SpeedBins
{
    private static readonly Dictionary<string, Ddr4SpeedBin> All;

    public static IReadOnlyList<string> Names { get; }

    static Ddr4SpeedBins()
    {
        static int[] R(int lo, int hi)
        {
            var a = new int[hi - lo + 1];
            for (int i = 0; i < a.Length; i++) a[i] = lo + i;
            return a;
        }

        All = new Dictionary<string, Ddr4SpeedBin>(StringComparer.OrdinalIgnoreCase)
        {
            ["DDR4-1600J"] = Bin("DDR4-1600J", 1600, 1.250, 10, [9, 10, 11, 12], 12.50, 35.00, 47.50, 5.00, 6.00, 25.00),
            ["DDR4-1600K"] = Bin("DDR4-1600K", 1600, 1.250, 11, [9, 11, 12], 13.75, 35.00, 48.75, 5.00, 6.00, 25.00),
            ["DDR4-1600L"] = Bin("DDR4-1600L", 1600, 1.250, 12, [10, 12], 15.00, 35.00, 50.00, 5.00, 6.00, 25.00),
            ["DDR4-1866L"] = Bin("DDR4-1866L", 1866, 1.071, 12, [9, 10, 12, 13, 14], 12.85, 34.00, 46.85, 4.20, 5.30, 23.00),
            ["DDR4-1866M"] = Bin("DDR4-1866M", 1866, 1.071, 13, [9, 11, 12, 13, 14], 13.92, 34.00, 47.92, 4.20, 5.30, 23.00),
            ["DDR4-1866N"] = Bin("DDR4-1866N", 1866, 1.071, 14, [10, 12, 14], 15.00, 34.00, 49.00, 4.20, 5.30, 23.00),
            ["DDR4-2133N"] = Bin("DDR4-2133N", 2133, 0.938, 14, [9, 10, 12, 14, 15, 16], 13.13, 33.00, 46.13, 3.70, 5.30, 21.00),
            ["DDR4-2133P"] = Bin("DDR4-2133P", 2133, 0.938, 15, [9, 11, 12, 13, 14, 15, 16], 14.06, 33.00, 47.06, 3.70, 5.30, 21.00),
            ["DDR4-2133R"] = Bin("DDR4-2133R", 2133, 0.938, 16, [10, 12, 14, 16], 15.00, 33.00, 48.00, 3.70, 5.30, 21.00),
            ["DDR4-2400P"] = Bin("DDR4-2400P", 2400, 0.833, 15, R(9, 18), 12.50, 32.00, 44.50, 3.30, 4.90, 21.00),
            ["DDR4-2400R"] = Bin("DDR4-2400R", 2400, 0.833, 16, R(9, 18), 13.33, 32.00, 45.32, 3.30, 4.90, 21.00),
            ["DDR4-2400T"] = Bin("DDR4-2400T", 2400, 0.833, 17, [10, 11, 12, 14, 16, 18], 14.16, 32.00, 46.16, 3.30, 4.90, 21.00),
            ["DDR4-2400U"] = Bin("DDR4-2400U", 2400, 0.833, 18, [10, 12, 14, 16, 18], 15.00, 32.00, 47.00, 3.30, 4.90, 21.00),
            ["DDR4-2666T"] = Bin("DDR4-2666T", 2666, 0.750, 17, R(9, 20), 12.75, 32.00, 44.75, 3.00, 4.90, 21.00),
            ["DDR4-2666U"] = Bin("DDR4-2666U", 2666, 0.750, 18, R(9, 20), 13.50, 32.00, 45.50, 3.00, 4.90, 21.00),
            ["DDR4-2666V"] = Bin("DDR4-2666V", 2666, 0.750, 19, R(10, 20), 14.25, 32.00, 46.25, 3.00, 4.90, 21.00),
            ["DDR4-2666W"] = Bin("DDR4-2666W", 2666, 0.750, 20, R(10, 20), 15.00, 32.00, 47.00, 3.00, 4.90, 21.00),
            ["DDR4-2933V"] = Bin("DDR4-2933V", 2933, 0.682, 19, R(10, 21), 12.96, 32.00, 44.96, 3.00, 4.90, 21.00),
            ["DDR4-2933W"] = Bin("DDR4-2933W", 2933, 0.682, 20, R(10, 21), 13.64, 32.00, 45.64, 3.00, 4.90, 21.00),
            ["DDR4-2933Y"] = Bin("DDR4-2933Y", 2933, 0.682, 21, R(10, 24), 14.32, 32.00, 46.32, 3.00, 4.90, 21.00),
            ["DDR4-2933AA"] = Bin("DDR4-2933AA", 2933, 0.682, 22, R(10, 24), 15.00, 32.00, 47.00, 3.00, 4.90, 21.00),
            ["DDR4-3200W"] = Bin("DDR4-3200W", 3200, 0.625, 20, R(10, 24), 12.50, 32.00, 44.50, 3.00, 4.90, 21.00),
            ["DDR4-3200AA"] = Bin("DDR4-3200AA", 3200, 0.625, 22, R(10, 24), 13.75, 32.00, 45.75, 3.00, 4.90, 21.00),
            ["DDR4-3200AC"] = Bin("DDR4-3200AC", 3200, 0.625, 24, R(10, 24), 15.00, 32.00, 47.00, 3.00, 4.90, 21.00),
        };
        Names = All.Keys.ToList();
    }

    private static Ddr4SpeedBin Bin(
        string name, int mt, double tckMin, int cl, int[] supportedCl,
        double taa, double tras, double trc, double rrds, double rrdl, double faw) => new()
    {
        Name = name,
        MtS = mt,
        TckMinNs = tckMin,
        TckMaxNs = 1.600,
        Cl = cl,
        SupportedCl = supportedCl,
        TaaNs = taa,
        TrcdNs = taa,
        TrpNs = taa,
        TrasNs = tras,
        TrcNs = trc,
        TwrNs = 15.00,
        TwtrSNs = 2.50,
        TwtrLNs = 7.50,
        TrrdSNs = rrds,
        TrrdLNs = rrdl,
        Trfc1Ns = 350.0,
        Trfc2Ns = 260.0,
        Trfc4Ns = 160.0,
        TfawNs = faw,
    };

    public static bool TryGet(string name, out Ddr4SpeedBin bin) =>
        All.TryGetValue(name, out bin!);

    /// <summary>Port of apply_ddr4_speed_bin / apply_ddr4_speed_bin_to_xmp.</summary>
    public static bool TryApply(string binName, Ddr4ProfileKind kind, out Ddr4ProfileEdit edit, out string error)
    {
        edit = new Ddr4ProfileEdit();
        error = "";
        if (!TryGet(binName, out var b))
        {
            error = $"未知 Speed Bin: {binName}";
            return false;
        }

        static int NsToTicks(double ns) =>
            (int)(ns / SpdUtils.Ddr4MtbNs + 0.9999);

        int tck = NsToTicks(b.TckMinNs);
        edit.TckMtb = tck;
        edit.TckFc = 0;
        edit.TckPs = SpdUtils.TicksToPsDdr4(tck);
        edit.SupportedCas = new HashSet<int>(b.SupportedCl);
        edit.ClTicks = NsToTicks(b.TaaNs);
        edit.ClFc = 0;
        edit.RcdTicks = NsToTicks(b.TrcdNs);
        edit.RcdFc = 0;
        edit.RpTicks = NsToTicks(b.TrpNs);
        edit.RpFc = 0;
        edit.RasTicks = NsToTicks(b.TrasNs);
        edit.RcTicks = NsToTicks(b.TrcNs);
        edit.RcFc = 0;
        edit.WrTicks = NsToTicks(b.TwrNs);
        edit.Rfc1Ticks = NsToTicks(b.Trfc1Ns);
        edit.Rfc2Ticks = NsToTicks(b.Trfc2Ns);
        edit.Rfc4Ticks = NsToTicks(b.Trfc4Ns);
        edit.FawTicks = NsToTicks(b.TfawNs);
        edit.RrdsTicks = NsToTicks(b.TrrdSNs);
        edit.RrdlTicks = NsToTicks(b.TrrdLNs);

        if (kind == Ddr4ProfileKind.Jedec)
        {
            edit.RrdsFc = (int)Math.Round((b.TrrdSNs - edit.RrdsTicks * SpdUtils.Ddr4MtbNs) * 1000);
            edit.RrdlFc = (int)Math.Round((b.TrrdLNs - edit.RrdlTicks * SpdUtils.Ddr4MtbNs) * 1000);
            edit.WtrsTicks = NsToTicks(b.TwtrSNs);
            edit.WtrlTicks = NsToTicks(b.TwtrLNs);
            edit.CcdlTicks = NsToTicks(Math.Max(b.TrrdLNs, 5.0));
        }
        else
        {
            edit.RrdsFc = 0;
            edit.RrdlFc = 0;
            edit.VddCv = 120;
            // XMP CAS bitmap only covers CL7–30
            edit.SupportedCas.RemoveWhere(cl => cl < 7 || cl > 30);
        }

        return true;
    }
}

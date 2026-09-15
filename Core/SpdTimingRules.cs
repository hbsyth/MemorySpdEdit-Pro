namespace SpdEditor.Core;

/// <summary>
/// JEDEC / Intel XMP 时序换算与写回规范化。
/// <list type="bullet">
/// <item>DDR5：JESD400-5B，nCK = floor(t×0.997/tCK)+1（0.30% 修正）</item>
/// <item>DDR4：JEDEC 21-C Annex L，时间 = MTB(0.125ns)+FTB(1ps)；nCK = ceil(t/tCK)</item>
/// <item>DDR3：Annex K，MTB(Byte10/11)+FTB(1ps)；nCK = ceil(t/tCK)（与 DDR4 相同，勿用 0.30%）</item>
/// </list>
/// </summary>
public static class SpdTimingRules
{
    public const int Ddr5CasMin = 20;
    public const int Ddr5CasMax = 98;
    public const int Ddr4CasMin = 7;
    public const int Ddr4CasMax = 36;
    public const int Ddr4XmpCasMax = 30;
    public const int Ddr3CasMin = 4;
    public const int Ddr3CasMax = 18;

    public const int VoltageCvMin = 110;
    public const int VoltageCvMax = 300;

    /// <summary>DDR4：时间(ps) → DRAM nCK（向上取整；勿用 DDR5 的 0.30% 因子）。</summary>
    public static int TimeToTicksDdr4(int timePs, int minCyclePs)
    {
        if (minCyclePs <= 0 || timePs <= 0) return 0;
        return (timePs + minCyclePs - 1) / minCyclePs;
    }

    /// <summary>DDR3：时间(ps) → nCK（ceil；与 DDR4 相同公式）。</summary>
    public static int TimeToTicksDdr3(int timePs, int minCyclePs) =>
        TimeToTicksDdr4(timePs, minCyclePs);

    /// <summary>DDR4：nCK → 时间(ps)。</summary>
    public static int TicksToTimeDdr4(int ticks, int minCyclePs) =>
        minCyclePs <= 0 || ticks <= 0 ? 0 : ticks * minCyclePs;

    /// <summary>将任意 ps 拟合为可写入的 (MTB ticks, FTB)，FTB∈[-128,127]。</summary>
    public static (int Ticks, int Fc) FitPsToMtbFtb(int ps)
    {
        if (ps <= 0) return (0, 0);
        int ticks = SpdUtils.PsToMtbTicksDdr4(ps);
        int fc = ps - SpdUtils.TicksToPsDdr4(ticks);

        while (fc < -128 && ticks > 1)
        {
            ticks--;
            fc = ps - SpdUtils.TicksToPsDdr4(ticks);
        }
        while (fc > 127)
        {
            ticks++;
            if (ticks > 65535) break;
            fc = ps - SpdUtils.TicksToPsDdr4(ticks);
        }

        return (Math.Clamp(ticks, 0, 65535), Math.Clamp(fc, -128, 127));
    }

    public static int ClampVoltageCv(int cv) =>
        Math.Clamp(cv, VoltageCvMin, VoltageCvMax);

    public static int ClampU16(int v) => Math.Clamp(v, 0, 0xFFFF);
    public static int ClampU8(int v) => Math.Clamp(v, 0, 255);

    /// <summary>规范化 DDR5 编辑结果，保证写入 SPD/XMP 字段合法。</summary>
    public static void NormalizeDdr5(Ddr5ProfileEdit e)
    {
        e.TckPs = Math.Clamp(e.TckPs, 1, 0xFFFF);
        e.TclPs = ClampU16(e.TclPs);
        e.TrcdPs = ClampU16(e.TrcdPs);
        e.TrpPs = ClampU16(e.TrpPs);
        e.TrasPs = ClampU16(e.TrasPs);
        e.TrcPs = ClampU16(e.TrcPs);
        e.TwrPs = ClampU16(e.TwrPs);
        e.Trfc1Ns = ClampU16(e.Trfc1Ns);
        e.Trfc2Ns = ClampU16(e.Trfc2Ns);
        e.TrfcsbNs = ClampU16(e.TrfcsbNs);

        e.TrrdlPs = ClampU16(e.TrrdlPs);
        e.TccdLPs = ClampU16(e.TccdLPs);
        e.TccdLWrPs = ClampU16(e.TccdLWrPs);
        e.TccdLWr2Ps = ClampU16(e.TccdLWr2Ps);
        e.TfawPs = ClampU16(e.TfawPs);
        e.TccdLWtrPs = ClampU16(e.TccdLWtrPs);
        e.TccdSWtrPs = ClampU16(e.TccdSWtrPs);
        e.TrtpPs = ClampU16(e.TrtpPs);

        e.VddCv = ClampVoltageCv(e.VddCv <= 0 ? 110 : e.VddCv);
        e.VddqCv = ClampVoltageCv(e.VddqCv <= 0 ? 110 : e.VddqCv);
        e.VppCv = ClampVoltageCv(e.VppCv <= 0 ? 180 : e.VppCv);
        e.VmemCv = ClampVoltageCv(e.VmemCv <= 0 ? 110 : e.VmemCv);
        e.CommandRate = Math.Clamp(e.CommandRate, 0, 3);
        e.SupportedCas = NormalizeDdr5Cas(e.SupportedCas);

        if (e.TrcPs > 0 && e.TrasPs > 0 && e.TrcPs < e.TrasPs)
            e.TrcPs = e.TrasPs;

        // Lower Limit ≥ 由时间算出的 nCK（与主板 max(nCK, limit) 语义一致）
        int tck = e.TckPs;
        if (tck > 0)
        {
            e.TrrdlMin = ClampU8(Math.Max(e.TrrdlMin, SpdUtils.TimeToTicksDdr5(e.TrrdlPs, tck)));
            e.TccdLMin = ClampU8(Math.Max(e.TccdLMin, SpdUtils.TimeToTicksDdr5(e.TccdLPs, tck)));
            e.TccdLWrMin = ClampU8(Math.Max(e.TccdLWrMin, SpdUtils.TimeToTicksDdr5(e.TccdLWrPs, tck)));
            e.TccdLWr2Min = ClampU8(Math.Max(e.TccdLWr2Min, SpdUtils.TimeToTicksDdr5(e.TccdLWr2Ps, tck)));
            e.TfawMin = ClampU8(Math.Max(e.TfawMin, SpdUtils.TimeToTicksDdr5(e.TfawPs, tck)));
            e.TccdLWtrMin = ClampU8(Math.Max(e.TccdLWtrMin, SpdUtils.TimeToTicksDdr5(e.TccdLWtrPs, tck)));
            e.TccdSWtrMin = ClampU8(Math.Max(e.TccdSWtrMin, SpdUtils.TimeToTicksDdr5(e.TccdSWtrPs, tck)));
            e.TrtpMin = ClampU8(Math.Max(e.TrtpMin, SpdUtils.TimeToTicksDdr5(e.TrtpPs, tck)));
        }
        else
        {
            e.TrrdlMin = ClampU8(e.TrrdlMin);
            e.TccdLMin = ClampU8(e.TccdLMin);
            e.TccdLWrMin = ClampU8(e.TccdLWrMin);
            e.TccdLWr2Min = ClampU8(e.TccdLWr2Min);
            e.TfawMin = ClampU8(e.TfawMin);
            e.TccdLWtrMin = ClampU8(e.TccdLWtrMin);
            e.TccdSWtrMin = ClampU8(e.TccdSWtrMin);
            e.TrtpMin = ClampU8(e.TrtpMin);
        }

        if (e.ProfileName.Length > 16)
            e.ProfileName = e.ProfileName[..16];
    }

    /// <summary>规范化 DDR4 编辑结果（MTB/FTB 可表示、CAS/电压合法）。</summary>
    public static void NormalizeDdr4(Ddr4ProfileEdit e, Ddr4ProfileKind kind)
    {
        if (e.TckPs > 0)
        {
            var (tm, tf) = FitPsToMtbFtb(e.TckPs);
            e.TckMtb = ClampU8(tm);
            e.TckFc = tf;
            e.TckPs = SpdUtils.TicksToPsDdr4(e.TckMtb) + e.TckFc;
        }
        else
        {
            e.TckMtb = ClampU8(e.TckMtb);
            e.TckFc = Math.Clamp(e.TckFc, -128, 127);
            e.TckPs = SpdUtils.TicksToPsDdr4(e.TckMtb) + e.TckFc;
        }
        if (e.TckPs < 1)
        {
            e.TckMtb = 5;
            e.TckFc = 0;
            e.TckPs = 625;
        }

        static (int Mtb, int Fc) FitByte(int ps)
        {
            var (t, f) = FitPsToMtbFtb(ps);
            return (ClampU8(t), f);
        }

        (e.ClTicks, e.ClFc) = FitByte(SpdUtils.TicksToPsDdr4(e.ClTicks) + e.ClFc);
        (e.RcdTicks, e.RcdFc) = FitByte(SpdUtils.TicksToPsDdr4(e.RcdTicks) + e.RcdFc);
        (e.RpTicks, e.RpFc) = FitByte(SpdUtils.TicksToPsDdr4(e.RpTicks) + e.RpFc);

        e.RasTicks = Math.Clamp(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.RasTicks)), 0, 0xFFF);
        {
            var (t, f) = FitPsToMtbFtb(SpdUtils.TicksToPsDdr4(e.RcTicks) + e.RcFc);
            e.RcTicks = Math.Clamp(t, 0, 0xFFF);
            e.RcFc = f;
        }

        int wrBase = e.WrTicks > 0 ? e.WrTicks : SpdUtils.PsToMtbTicksDdr4(15_000);
        e.WrTicks = Math.Clamp(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(wrBase)), 0, 0xFFF);

        e.Rfc1Ticks = ClampU16(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.Rfc1Ticks)));
        e.Rfc2Ticks = ClampU16(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.Rfc2Ticks)));
        e.Rfc4Ticks = ClampU16(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.Rfc4Ticks)));
        e.FawTicks = Math.Clamp(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.FawTicks)), 0, 0xFFF);

        (e.RrdsTicks, e.RrdsFc) = FitByte(SpdUtils.TicksToPsDdr4(e.RrdsTicks) + e.RrdsFc);
        (e.RrdlTicks, e.RrdlFc) = FitByte(SpdUtils.TicksToPsDdr4(e.RrdlTicks) + e.RrdlFc);
        (e.CcdlTicks, e.CcdlFc) = FitByte(SpdUtils.TicksToPsDdr4(e.CcdlTicks) + e.CcdlFc);

        e.WtrsTicks = Math.Clamp(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.WtrsTicks)), 0, 0xFFF);
        e.WtrlTicks = Math.Clamp(SpdUtils.PsToMtbTicksDdr4(SpdUtils.TicksToPsDdr4(e.WtrlTicks)), 0, 0xFFF);

        if (e.RasTicks > 0 && e.RcTicks > 0 && e.RcTicks < e.RasTicks)
            e.RcTicks = e.RasTicks;

        e.VddCv = e.VddCv <= 0 ? 120 : ClampVoltageCv(e.VddCv);
        int maxCl = kind == Ddr4ProfileKind.Xmp ? Ddr4XmpCasMax : Ddr4CasMax;
        e.SupportedCas = NormalizeDdr4Cas(e.SupportedCas, maxCl);

        if (kind == Ddr4ProfileKind.Xmp)
            e.WrTicks = SpdUtils.PsToMtbTicksDdr4(15_000);
    }

    public static HashSet<int> NormalizeDdr5Cas(HashSet<int> src)
    {
        var set = new HashSet<int>();
        foreach (int cl in src)
        {
            if (cl is >= Ddr5CasMin and <= Ddr5CasMax && cl % 2 == 0)
                set.Add(cl);
        }
        return set;
    }

    public static HashSet<int> NormalizeDdr4Cas(HashSet<int> src, int maxCl)
    {
        var set = new HashSet<int>();
        foreach (int cl in src)
        {
            if (cl >= Ddr4CasMin && cl <= maxCl)
                set.Add(cl);
        }
        return set;
    }
}

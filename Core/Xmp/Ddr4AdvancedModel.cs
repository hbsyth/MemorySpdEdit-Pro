namespace SpdEditor.Core.Xmp;

public sealed class Ddr4AdvancedModel
{
    public const int TotalSize = 512;
    public const int XmpOffset = 0x180;
    public const int XmpProfileOffset = 0x189;
    public const int XmpProfileSize = 0x2F;

    private byte[] _data;

    public bool XmpFound { get; set; }
    public Ddr4XmpProfile Xmp1 { get; private set; }
    public Ddr4XmpProfile Xmp2 { get; private set; }

    public Ddr4AdvancedModel(byte[] data)
    {
        _data = new byte[TotalSize];
        Array.Copy(data, _data, Math.Min(data.Length, TotalSize));
        if (_data[2] != 0x0C)
            throw new InvalidDataException($"不是 DDR4 SPD (Byte2=0x{_data[2]:X2})");
        Xmp1 = new Ddr4XmpProfile(1);
        Xmp2 = new Ddr4XmpProfile(2);
        ParseXmp();
    }

    public byte[] GetBytes()
    {
        UpdateCrc();
        var result = (byte[])_data.Clone();
        if (XmpFound)
        {
            result[XmpOffset] = 0x0C;
            result[XmpOffset + 1] = 0x4A;
            byte enabled = 0;
            if (!Xmp1.IsEmpty) enabled |= 0x1;
            if (!Xmp2.IsEmpty) enabled |= 0x2;
            result[XmpOffset + 2] = enabled;
            result[XmpOffset + 3] = 0x20;
            Array.Copy(Xmp1.GetBytes(), 0, result, XmpProfileOffset, XmpProfileSize);
            Array.Copy(Xmp2.GetBytes(), 0, result, XmpProfileOffset + XmpProfileSize, XmpProfileSize);
        }
        return result;
    }

    public void UpdateCrc()
    {
        var crc = SpdUtils.Crc16Xmodem(_data.AsSpan(0, 0x7E));
        SpdUtils.SetWord(_data, 126, crc);
        var crc2 = SpdUtils.Crc16Xmodem(_data.AsSpan(0x80, 0x7E));
        SpdUtils.SetWord(_data, 254, crc2);
    }

    private void ParseXmp()
    {
        if (_data[XmpOffset] != 0x0C || _data[XmpOffset + 1] != 0x4A) return;
        XmpFound = true;
        Xmp1.Load(_data.AsSpan(XmpProfileOffset, XmpProfileSize));
        Xmp2.Load(_data.AsSpan(XmpProfileOffset + XmpProfileSize, XmpProfileSize));
    }

    // ---- JEDEC timing accessors ----
    public int MinCycleTicks
    {
        get => _data[18];
        set => _data[18] = (byte)value;
    }

    public int MinCycleFc
    {
        get => SpdUtils.SignedByte(_data[125]);
        set => _data[125] = (byte)value;
    }

    public int MinCycleTimePs
    {
        get => SpdUtils.TicksToPsDdr4(MinCycleTicks) + MinCycleFc;
        set
        {
            MinCycleTicks = SpdUtils.PsToTicksFcDdr4(value, out int fc);
            MinCycleFc = fc;
        }
    }

    public bool IsClSupported(int cl) => SpdUtils.IsClSupportedDdr4(_data, 20, cl);
    public void SetClSupported(int cl, bool v) => SpdUtils.SetClSupportedDdr4(_data, 20, cl, v);

    public int ClTicks { get => _data[24]; set => _data[24] = (byte)value; }
    public int RcdTicks { get => _data[25]; set => _data[25] = (byte)value; }
    public int RpTicks { get => _data[26]; set => _data[26] = (byte)value; }
    public int RasTicks
    {
        get => ((_data[27] & 0xF) << 8) | _data[28];
        set { _data[27] = (byte)((_data[27] & 0xF0) | ((value >> 8) & 0xF)); _data[28] = (byte)value; }
    }
    public int RcTicks
    {
        get => ((_data[27] & 0xF0) << 4) | _data[29];
        set { _data[27] = (byte)((_data[27] & 0x0F) | (((value >> 8) & 0xF) << 4)); _data[29] = (byte)value; }
    }
    public int Rfc1Ticks { get => SpdUtils.GetWord(_data, 30); set => SpdUtils.SetWord(_data, 30, value); }
    public int Rfc2Ticks { get => SpdUtils.GetWord(_data, 32); set => SpdUtils.SetWord(_data, 32, value); }
    public int Rfc4Ticks { get => SpdUtils.GetWord(_data, 34); set => SpdUtils.SetWord(_data, 34, value); }
    public int FawTicks
    {
        get => ((_data[36] & 0xF) << 8) | _data[37];
        set { _data[36] = (byte)((_data[36] & 0xF0) | ((value >> 8) & 0xF)); _data[37] = (byte)value; }
    }
    public int RrdsTicks { get => _data[38]; set => _data[38] = (byte)value; }
    public int RrdlTicks { get => _data[39]; set => _data[39] = (byte)value; }
    public int WrTicks
    {
        get => ((_data[41] & 0xF) << 8) | _data[42];
        set { _data[41] = (byte)((_data[41] & 0xF0) | ((value >> 8) & 0xF)); _data[42] = (byte)value; }
    }
    public int WtrsTicks { get => _data[44]; set => _data[44] = (byte)value; }
    public int WtrlTicks { get => _data[45]; set => _data[45] = (byte)value; }

    public int GetTimingPs(string name) => name switch
    {
        "tAA" => SpdUtils.TicksToPsDdr4(ClTicks),
        "tRCD" => SpdUtils.TicksToPsDdr4(RcdTicks),
        "tRP" => SpdUtils.TicksToPsDdr4(RpTicks),
        "tRAS" => SpdUtils.TicksToPsDdr4(RasTicks),
        "tRC" => SpdUtils.TicksToPsDdr4(RcTicks),
        "tWR" => SpdUtils.TicksToPsDdr4(WrTicks),
        "tRFC1" => (int)(Rfc1Ticks * SpdUtils.Ddr4MtbNs * 1000),
        "tRFC2" => (int)(Rfc2Ticks * SpdUtils.Ddr4MtbNs * 1000),
        "tRFC4" => (int)(Rfc4Ticks * SpdUtils.Ddr4MtbNs * 1000),
        "tRRD_S" => SpdUtils.TicksToPsDdr4(RrdsTicks),
        "tRRD_L" => SpdUtils.TicksToPsDdr4(RrdlTicks),
        "tFAW" => SpdUtils.TicksToPsDdr4(FawTicks),
        _ => 0,
    };

    public static readonly string[] FormFactors = ["RESERVED", "RDIMM", "UDIMM", "SODIMM", "LRDIMM", "CUDIMM", "CSODIMM"];

    public string FormFactor
    {
        get
        {
            int idx = _data[3] & 0xF;
            return idx < FormFactors.Length ? FormFactors[idx] : "UDIMM";
        }
        set
        {
            int idx = Array.IndexOf(FormFactors, value);
            if (idx < 0) idx = 2;
            _data[3] = (byte)((_data[3] & 0xF0) | (idx & 0xF));
        }
    }

    public string DensityLabel => (_data[4] & 0xF) switch
    {
        0 => "256Mb", 1 => "512Mb", 2 => "1Gb", 3 => "2Gb", 4 => "4Gb", 5 => "8Gb", 6 => "16Gb", 7 => "32Gb", _ => "Unknown",
    };

    public int BankGroups => ((_data[4] >> 6) & 1) == 0 ? 2 : 4;
    public int DeviceWidth => (_data[12] & 0x7) switch { 0 => 4, 1 => 8, 2 => 16, 3 => 32, _ => 8 };

    public int ManufacturingYear
    {
        get => _data[323];
        set => _data[323] = (byte)Math.Clamp(value, 0, 99);
    }

    public int ManufacturingWeek
    {
        get => _data[324];
        set => _data[324] = (byte)Math.Clamp(value, 0, 52);
    }

    public string PartNumber
    {
        get
        {
            var chars = new char[20];
            for (int i = 0; i < 20; i++)
            {
                byte b = _data[329 + i];
                if (b == 0) break;
                chars[i] = (char)b;
            }
            return new string(chars).TrimEnd('\0', ' ');
        }
        set
        {
            for (int i = 0; i < 20; i++)
                _data[329 + i] = i < value.Length ? (byte)value[i] : (byte)0x20;
        }
    }

    public Ddr4XmpProfile? GetXmpProfile(int no) => no switch { 1 => Xmp1, 2 => Xmp2, _ => null };

    public void SetXmpProfile(int no, Ddr4XmpProfile profile)
    {
        if (no == 1) Xmp1 = profile;
        else if (no == 2) Xmp2 = profile;
        XmpFound = true;
    }

    public bool CopyXmpProfile(int source, int target)
    {
        if (source == target || source is < 1 or > 2 || target is < 1 or > 2) return false;
        var src = GetXmpProfile(source)!;
        var clone = new Ddr4XmpProfile(target);
        clone.Load(src.GetBytes());
        if (target == 1) Xmp1 = clone; else Xmp2 = clone;
        XmpFound = true;
        return true;
    }

    public void SetTimingTicks(string name, int ticks) => _ = name switch
    {
        "tAA" => ClTicks = ticks,
        "tRCD" => RcdTicks = ticks,
        "tRP" => RpTicks = ticks,
        "tRAS" => RasTicks = ticks,
        "tRC" => RcTicks = ticks,
        "tWR" => WrTicks = ticks,
        "tRFC1" => Rfc1Ticks = ticks,
        "tRFC2" => Rfc2Ticks = ticks,
        "tRFC4" => Rfc4Ticks = ticks,
        "tRRD_S" => RrdsTicks = ticks,
        "tRRD_L" => RrdlTicks = ticks,
        "tFAW" => FawTicks = ticks,
        _ => 0,
    };
}

public sealed class Ddr4XmpProfile
{
    private readonly byte[] _data = new byte[0x2F];
    public int ProfileNo { get; }

    public Ddr4XmpProfile(int profileNo) => ProfileNo = profileNo;

    public bool IsEmpty => _data.All(b => b == 0);

    public void Load(ReadOnlySpan<byte> src) => src.CopyTo(_data);

    public byte[] GetBytes() => (byte[])_data.Clone();

    public int MinCycleTimePs
    {
        get => SpdUtils.TicksToPsDdr4(_data[2]) + SpdUtils.SignedByte(_data[3]);
        set
        {
            _data[2] = (byte)SpdUtils.PsToTicksFcDdr4(value, out int fc);
            _data[3] = (byte)fc;
        }
    }

    public int VddCentivolts
    {
        get
        {
            byte v = _data[0x1C];
            int hundredths = v & 0x7F;
            int ones = (v & 0x80) >> 7;
            return ones * 100 + hundredths;
        }
        set
        {
            int ones = value / 100;
            int hundredths = value % 100;
            _data[0x1C] = (byte)((ones != 0 ? 0x80 : 0) | (hundredths & 0x7F));
        }
    }

    public bool IsClSupported(int cl) => SpdUtils.IsClSupportedDdr4(_data, 4, cl);
    public void SetClSupported(int cl, bool v) => SpdUtils.SetClSupportedDdr4(_data, 4, cl, v);

    public int ClTicks { get => _data[9]; set => _data[9] = (byte)value; }
    public int RcdTicks { get => _data[10]; set => _data[10] = (byte)value; }
    public int RpTicks { get => _data[11]; set => _data[11] = (byte)value; }
    public int RasTicks
    {
        get => ((_data[12] & 0xF) << 8) | _data[13];
        set { _data[12] = (byte)((_data[12] & 0xF0) | ((value >> 8) & 0xF)); _data[13] = (byte)value; }
    }
    public int RcTicks
    {
        get => ((_data[12] & 0xF0) << 4) | _data[14];
        set { _data[12] = (byte)((_data[12] & 0x0F) | (((value >> 8) & 0xF) << 4)); _data[14] = (byte)value; }
    }
    public int Rfc1Ticks { get => SpdUtils.GetWord(_data, 15); set => SpdUtils.SetWord(_data, 15, value); }
    public int Rfc2Ticks { get => SpdUtils.GetWord(_data, 17); set => SpdUtils.SetWord(_data, 17, value); }
    public int Rfc4Ticks { get => SpdUtils.GetWord(_data, 19); set => SpdUtils.SetWord(_data, 19, value); }
    public int FawTicks
    {
        get => ((_data[21] & 0xF) << 8) | _data[22];
        set { _data[21] = (byte)((_data[21] & 0xF0) | ((value >> 8) & 0xF)); _data[22] = (byte)value; }
    }
    public int RrdsTicks { get => _data[23]; set => _data[23] = (byte)value; }
    public int RrdlTicks { get => _data[24]; set => _data[24] = (byte)value; }
    public int WrTicks
    {
        get => ((_data[25] & 0xF) << 8) | _data[26];
        set { _data[25] = (byte)((_data[25] & 0xF0) | ((value >> 8) & 0xF)); _data[26] = (byte)value; }
    }

    public static Ddr4XmpProfile FromBytes(int profileNo, byte[] data)
    {
        var p = new Ddr4XmpProfile(profileNo);
        p.Load(data);
        return p;
    }

    public void LoadSample()
    {
        Array.Clear(_data);
        MinCycleTimePs = 625;
        VddCentivolts = 120;
        ClTicks = RcdTicks = RpTicks = 18;
        RasTicks = 52;
        RcTicks = 70;
    }
}

namespace SpdEditor.Core.Xmp;

public sealed class Ddr5AdvancedModel
{
    public const int TotalSize = 1024;
    public const int XmpOffset = 0x280;
    public const int XmpHeaderSize = 0x40;
    public const int XmpProfileSize = 0x40;
    public static readonly int[] XmpProfileOffsets = [0x2C0, 0x300, 0x340, 0x380, 0x3C0];
    public const int ExpoOffset = 0x340;
    public const int ExpoSize = 0x80;

    private byte[] _data;
    private byte[] _xmpHeader = new byte[XmpHeaderSize];
    private byte[] _expoRaw = new byte[ExpoSize];

    public bool XmpFound { get; private set; }
    public bool ExpoFound { get; private set; }
    public Ddr5XmpProfile Xmp1 { get; private set; }
    public Ddr5XmpProfile Xmp2 { get; private set; }
    public Ddr5XmpProfile Xmp3 { get; private set; }
    public Ddr5XmpProfile XmpUser1 { get; private set; }
    public Ddr5XmpProfile XmpUser2 { get; private set; }
    public ExpoProfile Expo1 { get; private set; }
    public ExpoProfile Expo2 { get; private set; }

    public Ddr5AdvancedModel(byte[] data)
    {
        _data = new byte[TotalSize];
        Array.Copy(data, _data, Math.Min(data.Length, TotalSize));
        if (_data[2] != 0x12)
            throw new InvalidDataException($"不是 DDR5 SPD (Byte2=0x{_data[2]:X2})");
        Xmp1 = new Ddr5XmpProfile(1);
        Xmp2 = new Ddr5XmpProfile(2);
        Xmp3 = new Ddr5XmpProfile(3);
        XmpUser1 = new Ddr5XmpProfile(4);
        XmpUser2 = new Ddr5XmpProfile(5);
        Expo1 = new ExpoProfile(1);
        Expo2 = new ExpoProfile(2);
        ParseExtensions();
    }

    private void ParseExtensions()
    {
        if (_data[XmpOffset] == 0x0C && _data[XmpOffset + 1] == 0x4A)
        {
            XmpFound = true;
            Array.Copy(_data, XmpOffset, _xmpHeader, 0, XmpHeaderSize);
            Xmp1.Load(_data.AsSpan(XmpProfileOffsets[0], XmpProfileSize));
            Xmp2.Load(_data.AsSpan(XmpProfileOffsets[1], XmpProfileSize));
        }
        if (_data.AsSpan(ExpoOffset, 4).SequenceEqual("EXPO"u8))
        {
            ExpoFound = true;
            Array.Copy(_data, ExpoOffset, _expoRaw, 0, ExpoSize);
            Expo1.Load(_expoRaw.AsSpan(0x0A, ExpoProfile.ExpoProfileSize));
            Expo2.Load(_expoRaw.AsSpan(0x0A + ExpoProfile.ExpoProfileSize, ExpoProfile.ExpoProfileSize));
        }
        else if (XmpFound)
        {
            Xmp3.Load(_data.AsSpan(XmpProfileOffsets[2], XmpProfileSize));
            XmpUser1.Load(_data.AsSpan(XmpProfileOffsets[3], XmpProfileSize));
            XmpUser2.Load(_data.AsSpan(XmpProfileOffsets[4], XmpProfileSize));
        }
    }

    public byte[] GetBytes()
    {
        UpdateCrc();
        var result = (byte[])_data.Clone();
        if (XmpFound)
        {
            Array.Copy(_xmpHeader, 0, result, XmpOffset, XmpHeaderSize);
            Array.Copy(Xmp1.GetBytes(), 0, result, XmpProfileOffsets[0], XmpProfileSize);
            Array.Copy(Xmp2.GetBytes(), 0, result, XmpProfileOffsets[1], XmpProfileSize);
            if (!ExpoFound)
            {
                Array.Copy(Xmp3.GetBytes(), 0, result, XmpProfileOffsets[2], XmpProfileSize);
                Array.Copy(XmpUser1.GetBytes(), 0, result, XmpProfileOffsets[3], XmpProfileSize);
                Array.Copy(XmpUser2.GetBytes(), 0, result, XmpProfileOffsets[4], XmpProfileSize);
            }
        }
        if (ExpoFound)
        {
            Array.Copy(Expo1.GetBytes(), 0, _expoRaw, 0x0A, ExpoProfile.ExpoProfileSize);
            Array.Copy(Expo2.GetBytes(), 0, _expoRaw, 0x0A + ExpoProfile.ExpoProfileSize, ExpoProfile.ExpoProfileSize);
            Array.Copy(_expoRaw, 0, result, ExpoOffset, ExpoSize);
        }
        return result;
    }

    public void UpdateCrc()
    {
        var crc = SpdUtils.Crc16Xmodem(_data.AsSpan(0, 0x1FE));
        SpdUtils.SetWord(_data, 510, crc);
        if (XmpFound)
        {
            Xmp1.UpdateCrc();
            Xmp2.UpdateCrc();
            if (!ExpoFound)
            {
                Xmp3.UpdateCrc();
                XmpUser1.UpdateCrc();
                XmpUser2.UpdateCrc();
            }
        }
        if (ExpoFound)
        {
            var expoCrc = SpdUtils.Crc16Xmodem(_expoRaw.AsSpan(0, 0x7E));
            SpdUtils.SetWord(_expoRaw, 0x7E, expoCrc);
        }
    }

    public int MinCycleTime
    {
        get => SpdUtils.GetWord(_data, 20);
        set => SpdUtils.SetWord(_data, 20, value);
    }

    public int MaxCycleTime
    {
        get => SpdUtils.GetWord(_data, 22);
        set => SpdUtils.SetWord(_data, 22, value);
    }

    public bool IsClSupported(int cl) => SpdUtils.IsClSupportedDdr5(_data.AsSpan(24, 5).ToArray(), cl);
    public void SetClSupported(int cl, bool v) => SpdUtils.SetClSupportedDdr5(_data, 24, cl, v);

    public int TAA { get => SpdUtils.GetWord(_data, 30); set => SpdUtils.SetWord(_data, 30, value); }
    public int TRCD { get => SpdUtils.GetWord(_data, 32); set => SpdUtils.SetWord(_data, 32, value); }
    public int TRP { get => SpdUtils.GetWord(_data, 34); set => SpdUtils.SetWord(_data, 34, value); }
    public int TRAS { get => SpdUtils.GetWord(_data, 36); set => SpdUtils.SetWord(_data, 36, value); }
    public int TRC { get => SpdUtils.GetWord(_data, 38); set => SpdUtils.SetWord(_data, 38, value); }
    public int TWR { get => SpdUtils.GetWord(_data, 40); set => SpdUtils.SetWord(_data, 40, value); }
    public int TRFC1Slr { get => SpdUtils.GetWord(_data, 42); set => SpdUtils.SetWord(_data, 42, value); }
    public int TRFC2Slr { get => SpdUtils.GetWord(_data, 44); set => SpdUtils.SetWord(_data, 44, value); }
    public int TRFCsbSlr { get => SpdUtils.GetWord(_data, 46); set => SpdUtils.SetWord(_data, 46, value); }
    public int TRRD_L { get => SpdUtils.GetWord(_data, 70); set => SpdUtils.SetWord(_data, 70, value); }
    public int TRRD_L_LowerLimit { get => _data[72]; set => _data[72] = (byte)value; }
    public int TCCD_L { get => SpdUtils.GetWord(_data, 73); set => SpdUtils.SetWord(_data, 73, value); }
    public int TCCD_L_LowerLimit { get => _data[75]; set => _data[75] = (byte)value; }
    public int TCCD_L_WR { get => SpdUtils.GetWord(_data, 76); set => SpdUtils.SetWord(_data, 76, value); }
    public int TCCD_L_WR2 { get => SpdUtils.GetWord(_data, 79); set => SpdUtils.SetWord(_data, 79, value); }
    public int TFAW { get => SpdUtils.GetWord(_data, 82); set => SpdUtils.SetWord(_data, 82, value); }
    public int TFAW_LowerLimit { get => _data[84]; set => _data[84] = (byte)value; }
    public int TCCD_L_WTR { get => SpdUtils.GetWord(_data, 85); set => SpdUtils.SetWord(_data, 85, value); }
    public int TCCD_S_WTR { get => SpdUtils.GetWord(_data, 88); set => SpdUtils.SetWord(_data, 88, value); }
    public int TRTP { get => SpdUtils.GetWord(_data, 91); set => SpdUtils.SetWord(_data, 91, value); }

    public int GetTimingTicks(string name, int minCycle) => name switch
    {
        "tAA" => SpdUtils.GetTicksDdr5(_data, 30, minCycle),
        "tRCD" => SpdUtils.GetTicksDdr5(_data, 32, minCycle),
        "tRP" => SpdUtils.GetTicksDdr5(_data, 34, minCycle),
        "tRAS" => SpdUtils.GetTicksDdr5(_data, 36, minCycle),
        "tRC" => SpdUtils.GetTicksDdr5(_data, 38, minCycle),
        "tWR" => SpdUtils.GetTicksDdr5(_data, 40, minCycle),
        "tRFC1_slr" => SpdUtils.GetTicksDdr5(_data, 42, minCycle, 1000),
        "tRFC2_slr" => SpdUtils.GetTicksDdr5(_data, 44, minCycle, 1000),
        "tRFCsb_slr" => SpdUtils.GetTicksDdr5(_data, 46, minCycle, 1000),
        "tRRD_L" => SpdUtils.GetTicksDdr5(_data, 70, minCycle),
        "tCCD_L" => SpdUtils.GetTicksDdr5(_data, 73, minCycle),
        "tCCD_L_WR" => SpdUtils.GetTicksDdr5(_data, 76, minCycle),
        "tCCD_L_WR2" => SpdUtils.GetTicksDdr5(_data, 79, minCycle),
        "tFAW" => SpdUtils.GetTicksDdr5(_data, 82, minCycle),
        "tCCD_L_WTR" => SpdUtils.GetTicksDdr5(_data, 85, minCycle),
        "tCCD_S_WTR" => SpdUtils.GetTicksDdr5(_data, 88, minCycle),
        "tRTP" => SpdUtils.GetTicksDdr5(_data, 91, minCycle),
        _ => 0,
    };

    public void SetTimingPs(string name, int ps) => _ = name switch
    {
        "tAA" => TAA = ps,
        "tRCD" => TRCD = ps,
        "tRP" => TRP = ps,
        "tRAS" => TRAS = ps,
        "tRC" => TRC = ps,
        "tWR" => TWR = ps,
        "tRFC1_slr" => TRFC1Slr = ps,
        "tRFC2_slr" => TRFC2Slr = ps,
        "tRFCsb_slr" => TRFCsbSlr = ps,
        "tRRD_L" => TRRD_L = ps,
        "tCCD_L" => TCCD_L = ps,
        "tCCD_L_WR" => TCCD_L_WR = ps,
        "tCCD_L_WR2" => TCCD_L_WR2 = ps,
        "tFAW" => TFAW = ps,
        "tCCD_L_WTR" => TCCD_L_WTR = ps,
        "tCCD_S_WTR" => TCCD_S_WTR = ps,
        "tRTP" => TRTP = ps,
        _ => 0,
    };

    public void EnableXmp()
    {
        XmpFound = true;
        _xmpHeader[0] = 0x0C;
        _xmpHeader[1] = 0x4A;
        _xmpHeader[3] = 0x30;
        if (Xmp1.IsEmpty) Xmp1.LoadSample();
    }

    public void EnableExpo()
    {
        ExpoFound = true;
        "EXPO"u8.CopyTo(_expoRaw);
        _expoRaw[4] = 0x10;
        if (Expo1.IsEmpty) Expo1.LoadSample();
        UpdateExpoCrc();
    }

    // ---- Misc / 模组信息 ----
    public static readonly string[] FormFactors =
        ["RESERVED", "RDIMM", "UDIMM", "SODIMM", "LRDIMM", "CUDIMM", "CSODIMM", "MRDIMM", "CAMM2", "DDIMM", "SOLDER_DOWN"];

    public string FormFactor
    {
        get
        {
            int idx = _data[3] & 0xF;
            return idx < FormFactors.Length ? FormFactors[idx] : "RESERVED";
        }
        set
        {
            int idx = Array.IndexOf(FormFactors, value);
            if (idx < 0) idx = 2;
            _data[3] = (byte)((_data[3] & 0xF0) | (idx & 0xF));
        }
    }

    public int BankGroups
    {
        get => ((_data[7] >> 4) & 0x7) switch { 0 => 2, 1 => 4, 2 => 8, _ => 4 };
        set
        {
            int code = value switch { 2 => 0, 4 => 1, 8 => 2, _ => 1 };
            _data[7] = (byte)((_data[7] & 0x8F) | ((code & 0x7) << 4));
        }
    }

    public int DeviceWidth
    {
        get => (_data[234] & 0x7) switch { 0 => 4, 1 => 8, 2 => 16, 3 => 32, _ => 8 };
        set
        {
            int code = value switch { 4 => 0, 8 => 1, 16 => 2, 32 => 3, _ => 1 };
            _data[234] = (byte)((_data[234] & 0xF8) | (code & 0x7));
        }
    }

    public int ManufacturingYear
    {
        get => int.Parse(_data[515].ToString("X2"));
        set => _data[515] = (byte)Math.Clamp(value, 0, 99);
    }

    public int ManufacturingWeek
    {
        get => int.Parse(_data[516].ToString("X2"));
        set => _data[516] = (byte)Math.Clamp(value, 0, 52);
    }

    public string PartNumber
    {
        get => ReadAscii(_data, 521, 30);
        set => WriteAscii(_data, 521, 30, value);
    }

    public bool HeatSpreader
    {
        get => SpdUtils.GetBit(_data[233], 2);
        set => _data[233] = SpdUtils.SetBit(_data[233], 2, value);
    }

    public bool Expo1Enabled
    {
        get => ExpoFound && (_expoRaw[5] & 0x1) != 0;
        set { if (value) _expoRaw[5] |= 0x1; else _expoRaw[5] &= 0xFE; }
    }

    public bool Expo2Enabled
    {
        get => ExpoFound && (_expoRaw[5] & 0x2) != 0;
        set { if (value) _expoRaw[5] |= 0x2; else _expoRaw[5] &= 0xFD; }
    }

    public string XmpProfile1Name { get => ReadXmpName(14); set => WriteXmpName(14, value); }
    public string XmpProfile2Name { get => ReadXmpName(30); set => WriteXmpName(30, value); }
    public string XmpProfile3Name { get => ReadXmpName(46); set => WriteXmpName(46, value); }

    public bool Xmp1Enabled { get => (_xmpHeader[3] & 0x1) != 0; set => SetXmpEnabled(0, value); }
    public bool Xmp2Enabled { get => (_xmpHeader[3] & 0x2) != 0; set => SetXmpEnabled(1, value); }
    public bool Xmp3Enabled { get => (_xmpHeader[3] & 0x4) != 0; set => SetXmpEnabled(2, value); }

    public Ddr5XmpProfile? GetXmpProfile(int no) => no switch
    {
        1 => Xmp1, 2 => Xmp2, 3 => Xmp3, 4 => XmpUser1, 5 => XmpUser2, _ => null,
    };

    public void SetXmpProfile(int no, Ddr5XmpProfile profile)
    {
        switch (no)
        {
            case 1: Xmp1 = profile; Xmp1Enabled = true; break;
            case 2: Xmp2 = profile; Xmp2Enabled = true; break;
            case 3: Xmp3 = profile; Xmp3Enabled = true; break;
            case 4: XmpUser1 = profile; break;
            case 5: XmpUser2 = profile; break;
        }
        profile.UpdateCrc();
        XmpFound = true;
    }

    public bool CopyXmpProfile(int source, int target)
    {
        if (source == target) return false;
        var src = GetXmpProfile(source);
        if (src == null) return false;
        var clone = Ddr5XmpProfile.FromBytes(target, src.GetBytes());
        SetXmpProfile(target, clone);
        string name = source switch
        {
            1 => XmpProfile1Name, 2 => XmpProfile2Name, 3 => XmpProfile3Name,
            4 => "User 1", 5 => "User 2", _ => "Copied",
        };
        if (target == 1) XmpProfile1Name = name;
        else if (target == 2) XmpProfile2Name = name;
        else if (target == 3) XmpProfile3Name = name;
        UpdateXmpHeaderCrc();
        return true;
    }

    private void SetXmpEnabled(int bit, bool enabled)
    {
        if (enabled) _xmpHeader[3] = (byte)(_xmpHeader[3] | (1 << bit));
        else _xmpHeader[3] = (byte)(_xmpHeader[3] & ~(1 << bit));
        UpdateXmpHeaderCrc();
    }

    private void UpdateXmpHeaderCrc()
    {
        var crc = SpdUtils.Crc16Xmodem(_xmpHeader.AsSpan(0, 62));
        SpdUtils.SetWord(_xmpHeader, 62, crc);
    }

    private void UpdateExpoCrc()
    {
        var crc = SpdUtils.Crc16Xmodem(_expoRaw.AsSpan(0, 0x7E));
        SpdUtils.SetWord(_expoRaw, 0x7E, crc);
    }

    private string ReadXmpName(int offset) => ReadAscii(_xmpHeader, offset, 16);
    private void WriteXmpName(int offset, string value) => WriteAscii(_xmpHeader, offset, 16, value);

    private static string ReadAscii(byte[] data, int offset, int len)
    {
        var chars = new char[len];
        for (int i = 0; i < len && offset + i < data.Length; i++)
        {
            byte b = data[offset + i];
            if (b == 0) break;
            chars[i] = b >= 0x20 && b <= 0x7E ? (char)b : ' ';
        }
        return new string(chars).TrimEnd('\0', ' ');
    }

    private static void WriteAscii(byte[] data, int offset, int len, string value)
    {
        for (int i = 0; i < len; i++)
            data[offset + i] = i < value.Length ? (byte)value[i] : (byte)0;
    }
}

public sealed class Ddr5XmpProfile
{
    private readonly byte[] _data = new byte[0x40];
    public int ProfileNo { get; }

    public Ddr5XmpProfile(int profileNo) => ProfileNo = profileNo;

    public bool IsEmpty => _data.All(b => b == 0);
    public void Load(ReadOnlySpan<byte> src) => src.CopyTo(_data);
    public byte[] GetBytes() => (byte[])_data.Clone();

    public int Vdd { get => SpdUtils.VoltageByteToCentivolts(_data[1]); set => _data[1] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int Vddq { get => SpdUtils.VoltageByteToCentivolts(_data[2]); set => _data[2] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int Vpp { get => SpdUtils.VoltageByteToCentivolts(_data[0]); set => _data[0] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int Vmemctrl { get => SpdUtils.VoltageByteToCentivolts(_data[4]); set => _data[4] = SpdUtils.VoltageCentivoltsToByte(value); }

    public int MinCycleTime
    {
        get => SpdUtils.GetWord(_data, 5);
        set => SpdUtils.SetWord(_data, 5, value);
    }

    public bool IsClSupported(int cl) => SpdUtils.IsClSupportedDdr5(_data.AsSpan(7, 5).ToArray(), cl);
    public void SetClSupported(int cl, bool v) => SpdUtils.SetClSupportedDdr5(_data, 7, cl, v);

    public int TAA { get => SpdUtils.GetWord(_data, 13); set => SpdUtils.SetWord(_data, 13, value); }
    public int TRCD { get => SpdUtils.GetWord(_data, 15); set => SpdUtils.SetWord(_data, 15, value); }
    public int TRP { get => SpdUtils.GetWord(_data, 17); set => SpdUtils.SetWord(_data, 17, value); }
    public int TRAS { get => SpdUtils.GetWord(_data, 19); set => SpdUtils.SetWord(_data, 19, value); }
    public int TRC { get => SpdUtils.GetWord(_data, 21); set => SpdUtils.SetWord(_data, 21, value); }
    public int TWR { get => SpdUtils.GetWord(_data, 23); set => SpdUtils.SetWord(_data, 23, value); }
    public int TRFC1 { get => SpdUtils.GetWord(_data, 25); set => SpdUtils.SetWord(_data, 25, value); }
    public int TRFC2 { get => SpdUtils.GetWord(_data, 27); set => SpdUtils.SetWord(_data, 27, value); }
    public int TRFC { get => SpdUtils.GetWord(_data, 29); set => SpdUtils.SetWord(_data, 29, value); }
    public int TRRD_L { get => SpdUtils.GetWord(_data, 31); set => SpdUtils.SetWord(_data, 31, value); }
    public int TCCD_L { get => SpdUtils.GetWord(_data, 46); set => SpdUtils.SetWord(_data, 46, value); }
    public int TFAW { get => SpdUtils.GetWord(_data, 52); set => SpdUtils.SetWord(_data, 52, value); }
    public int TCCD_L_WTR { get => SpdUtils.GetWord(_data, 40); set => SpdUtils.SetWord(_data, 40, value); }
    public int TCCD_S_WTR { get => SpdUtils.GetWord(_data, 43); set => SpdUtils.SetWord(_data, 43, value); }
    public int TRTP { get => SpdUtils.GetWord(_data, 49); set => SpdUtils.SetWord(_data, 49, value); }

    public void UpdateCrc()
    {
        var crc = SpdUtils.Crc16Xmodem(_data.AsSpan(0, 62));
        SpdUtils.SetWord(_data, 62, crc);
    }

    public int CommandRate
    {
        get => _data[60] & 0xF;
        set => _data[60] = (byte)((_data[60] & 0xF0) | (value & 0xF));
    }

    public bool IntelDynamicMemoryBoost
    {
        get => SpdUtils.GetBit(_data[59], 0);
        set => _data[59] = SpdUtils.SetBit(_data[59], 0, value);
    }

    public bool RealtimeMemoryFrequencyOc
    {
        get => SpdUtils.GetBit(_data[59], 1);
        set => _data[59] = SpdUtils.SetBit(_data[59], 1, value);
    }

    public bool CheckCrcValidity()
    {
        int stored = SpdUtils.GetWord(_data, 62);
        int calc = SpdUtils.Crc16Xmodem(_data.AsSpan(0, 62));
        return stored == calc;
    }

    public static Ddr5XmpProfile FromBytes(int profileNo, byte[] data)
    {
        var p = new Ddr5XmpProfile(profileNo);
        p.Load(data);
        return p;
    }

    public void Wipe() => Array.Clear(_data);

    public void LoadSample()
    {
        Array.Clear(_data);
        MinCycleTime = 625;
        CommandRate = 2;
        Vdd = Vddq = Vmemctrl = 110;
        Vpp = 180;
        for (int cl = 20; cl <= 56; cl += 2) SetClSupported(cl, cl is 22 or 26 or 28 or 30 or 32 or 36 or 40 or 42 or 46 or 48 or 50 or 52 or 54 or 56);
        TAA = TRCD = TRP = 12500;
        TRAS = 32000;
        TRC = 44500;
        UpdateCrc();
    }
}

public sealed class ExpoProfile
{
    public const int ExpoProfileSize = 0x28;
    private readonly byte[] _data = new byte[ExpoProfileSize];
    public int ProfileNo { get; }

    public ExpoProfile(int profileNo) => ProfileNo = profileNo;

    public bool IsEmpty => _data.All(b => b == 0);
    public void Load(ReadOnlySpan<byte> src) => src.CopyTo(_data);
    public byte[] GetBytes() => (byte[])_data.Clone();

    public int Vdd { get => SpdUtils.VoltageByteToCentivolts(_data[0]); set => _data[0] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int Vddq { get => SpdUtils.VoltageByteToCentivolts(_data[1]); set => _data[1] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int Vpp { get => SpdUtils.VoltageByteToCentivolts(_data[2]); set => _data[2] = SpdUtils.VoltageCentivoltsToByte(value); }
    public int MinCycleTime { get => SpdUtils.GetWord(_data, 4); set => SpdUtils.SetWord(_data, 4, value); }
    public int TAA { get => SpdUtils.GetWord(_data, 6); set => SpdUtils.SetWord(_data, 6, value); }
    public int TRCD { get => SpdUtils.GetWord(_data, 8); set => SpdUtils.SetWord(_data, 8, value); }
    public int TRP { get => SpdUtils.GetWord(_data, 10); set => SpdUtils.SetWord(_data, 10, value); }
    public int TRAS { get => SpdUtils.GetWord(_data, 12); set => SpdUtils.SetWord(_data, 12, value); }
    public int TRC { get => SpdUtils.GetWord(_data, 14); set => SpdUtils.SetWord(_data, 14, value); }
    public int TWR { get => SpdUtils.GetWord(_data, 16); set => SpdUtils.SetWord(_data, 16, value); }
    public int TRFC1 { get => SpdUtils.GetWord(_data, 18); set => SpdUtils.SetWord(_data, 18, value); }
    public int TRFC2 { get => SpdUtils.GetWord(_data, 20); set => SpdUtils.SetWord(_data, 20, value); }
    public int TRFC { get => SpdUtils.GetWord(_data, 22); set => SpdUtils.SetWord(_data, 22, value); }
    public int TRRD_L { get => SpdUtils.GetWord(_data, 24); set => SpdUtils.SetWord(_data, 24, value); }
    public int TCCD_L { get => SpdUtils.GetWord(_data, 26); set => SpdUtils.SetWord(_data, 26, value); }
    public int TFAW { get => SpdUtils.GetWord(_data, 32); set => SpdUtils.SetWord(_data, 32, value); }
    public int TCCD_L_WTR { get => SpdUtils.GetWord(_data, 34); set => SpdUtils.SetWord(_data, 34, value); }
    public int TCCD_S_WTR { get => SpdUtils.GetWord(_data, 36); set => SpdUtils.SetWord(_data, 36, value); }
    public int TRTP { get => SpdUtils.GetWord(_data, 38); set => SpdUtils.SetWord(_data, 38, value); }

    public void LoadSample()
    {
        Array.Clear(_data);
        MinCycleTime = 625;
        Vdd = Vddq = 110;
        Vpp = 180;
        TAA = TRCD = TRP = 12500;
        TRAS = 32000;
        TRC = 44500;
    }
}

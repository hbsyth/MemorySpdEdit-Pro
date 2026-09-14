using SpdEditor.Core.Xmp;

namespace SpdEditor;

public sealed class AdvancedEditorForm : Form
{
    private readonly bool _isDdr5;
    private Ddr4AdvancedModel? _ddr4;
    private Ddr5AdvancedModel? _ddr5;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<int, CheckBox> _clChecks = new();

    public byte[] ResultData { get; private set; }

    private AdvancedEditorForm(byte[] data)
    {
        _isDdr5 = data.Length >= 1024 && data[2] == 0x12;
        ResultData = data;
        if (_isDdr5)
            _ddr5 = new Ddr5AdvancedModel(data);
        else
            _ddr4 = new Ddr4AdvancedModel(data.Length >= 512 ? data : Pad(data, 512));

        InitializeUi();
        LoadAllTabs();
    }

    public static bool TryEdit(IWin32Window owner, ref byte[] spdData)
    {
        try
        {
            using var form = new AdvancedEditorForm(spdData);
            if (form.ShowDialog(owner) != DialogResult.OK) return false;
            spdData = form.ResultData;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"无法打开高级编辑器: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void InitializeUi()
    {
        Text = _isDdr5
            ? "XMP/EXPO/频率/时序/电压修改 (DDR5)"
            : "XMP/频率/时序/电压修改 (DDR4)";
        Size = new Size(980, 720);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        MinimumSize = new Size(900, 650);

        _tabs.TabPages.Add(BuildJedecTab());

        if (_isDdr5 && _ddr5 != null)
        {
            _tabs.TabPages.Add(BuildDdr5XmpTab("XMP Profile 1", 1, _ddr5.Xmp1));
            _tabs.TabPages.Add(BuildDdr5XmpTab("XMP Profile 2", 2, _ddr5.Xmp2));
            _tabs.TabPages.Add(BuildDdr5XmpTab("XMP Profile 3", 3, _ddr5.Xmp3));
            _tabs.TabPages.Add(BuildDdr5XmpTab("XMP User 1", 4, _ddr5.XmpUser1));
            _tabs.TabPages.Add(BuildDdr5XmpTab("XMP User 2", 5, _ddr5.XmpUser2));
            _tabs.TabPages.Add(BuildExpoTab("EXPO Profile 1", 1, _ddr5.Expo1));
            _tabs.TabPages.Add(BuildExpoTab("EXPO Profile 2", 2, _ddr5.Expo2));
        }
        else if (_ddr4 != null)
        {
            _tabs.TabPages.Add(BuildDdr4XmpTab("XMP Profile 1", 1, _ddr4.Xmp1));
            _tabs.TabPages.Add(BuildDdr4XmpTab("XMP Profile 2", 2, _ddr4.Xmp2));
        }

        _tabs.TabPages.Add(BuildMiscTab());

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        var btnOk = new Button { Text = "应用修改", DialogResult = DialogResult.OK, Width = 100, Height = 32, Left = 760, Top = 8 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 100, Height = 32, Left = 870, Top = 8 };
        btnOk.Click += (_, _) => ApplyAndClose();
        bottom.Controls.AddRange([btnOk, btnCancel]);

        Controls.Add(_tabs);
        Controls.Add(bottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private TabPage BuildJedecTab()
    {
        var page = new TabPage("JEDEC SPD") { AutoScroll = true };
        var panel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

        var grpFreq = new GroupBox { Text = "频率 / Speed Bin", Width = 920, Height = 90, Left = 8, Top = 8 };
        var cmbBin = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Left = 90, Top = 24 };
        cmbBin.Items.Add("");
        foreach (var name in SpeedBinService.GetBinNames(_isDdr5))
            cmbBin.Items.Add(name);
        var numMct = AddNum(grpFreq, "Min Cycle (ps):", 90, 52, 65535, _isDdr5 ? _ddr5!.MinCycleTime : _ddr4!.MinCycleTimePs,
            v =>
            {
                if (_isDdr5) _ddr5!.MinCycleTime = (int)v;
                else _ddr4!.MinCycleTimePs = (int)v;
            });
        var lblFreq = new Label { Left = 320, Top = 54, Width = 280, Text = GetFreqLabel(numMct.Value) };
        numMct.ValueChanged += (_, _) => lblFreq.Text = GetFreqLabel(numMct.Value);
        var btnApply = new Button { Text = "Apply", Left = 280, Top = 22, Width = 70 };
        btnApply.Click += (_, _) =>
        {
            if (cmbBin.SelectedItem is not string bin || string.IsNullOrEmpty(bin)) return;
            if (_isDdr5) SpeedBinService.ApplyToDdr5Jedec(_ddr5!, bin);
            else SpeedBinService.ApplyToDdr4Jedec(_ddr4!, bin);
            numMct.Value = _isDdr5 ? _ddr5!.MinCycleTime : _ddr4!.MinCycleTimePs;
            LoadAllTabs();
        };
        grpFreq.Controls.AddRange([new Label { Text = "Speed Bin:", Left = 12, Top = 28 }, cmbBin, btnApply, lblFreq]);

        var grpCl = new GroupBox { Text = "CAS Latency", Width = 920, Height = 120, Left = 8, Top = 104 };
        _clChecks.Clear();
        var clValues = _isDdr5 ? Enumerable.Range(20, 40).Select(i => 20 + i * 2).ToArray() : Enumerable.Range(7, 30).ToArray();
        for (int i = 0; i < clValues.Length; i++)
        {
            int cl = clValues[i];
            var cb = new CheckBox { Text = cl.ToString(), Width = 48, Left = 12 + (i % 12) * 72, Top = 24 + (i / 12) * 28 };
            cb.CheckedChanged += (_, _) =>
            {
                if (_isDdr5) _ddr5!.SetClSupported(cl, cb.Checked);
                else _ddr4!.SetClSupported(cl, cb.Checked);
            };
            _clChecks[cl] = cb;
            grpCl.Controls.Add(cb);
        }

        var grpTim = new GroupBox { Text = "时序参数", Width = 920, Height = 280, Left = 8, Top = 232 };
        AddTimingFields(grpTim, _isDdr5
            ? ["tAA", "tRCD", "tRP", "tRAS", "tRC", "tWR", "tRFC1_slr", "tRFC2_slr", "tRFCsb_slr", "tRRD_L", "tCCD_L", "tFAW", "tCCD_L_WTR", "tCCD_S_WTR", "tRTP"]
            : ["tAA", "tRCD", "tRP", "tRAS", "tRC", "tWR", "tRFC1", "tRFC2", "tRFC4", "tRRD_S", "tRRD_L", "tFAW"]);

        panel.Controls.AddRange([grpFreq, grpCl, grpTim]);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildDdr4XmpTab(string title, int profileNo, Ddr4XmpProfile profile)
    {
        var page = new TabPage(title) { AutoScroll = true };
        var grp = new GroupBox { Text = title, Width = 920, Height = 360, Top = 8, Left = 8 };

        AddProfileIoButtons(grp, 12, 24, () => profile.GetBytes(), Ddr4AdvancedModel.XmpProfileSize,
            data => { _ddr4!.SetXmpProfile(profileNo, Ddr4XmpProfile.FromBytes(profileNo, data)); },
            () => XmpProfileService.ValidateDdr4Xmp(profile), $"XMP2_P{profileNo}");

        AddSpeedBinCombo(grp, 12, 52, false, bin => SpeedBinService.ApplyToDdr4Xmp(profile, bin));

        AddNum(grp, "Min Cycle (ps):", 90, 80, 65535, profile.MinCycleTimePs, v => profile.MinCycleTimePs = (int)v);
        AddNum(grp, "VDD (cV):", 320, 80, 300, profile.VddCentivolts, v => profile.VddCentivolts = (int)v);
        AddTimingRowDdr4(grp, profile, 118);
        if (_ddr4 != null && profile.IsEmpty) { profile.LoadSample(); _ddr4.XmpFound = true; }

        page.Controls.Add(grp);
        return page;
    }

    private TabPage BuildDdr5XmpTab(string title, int profileNo, Ddr5XmpProfile profile)
    {
        var page = new TabPage(title) { AutoScroll = true };
        var grp = new GroupBox { Text = title, Width = 920, Height = 420, Top = 8, Left = 8 };

        AddProfileIoButtons(grp, 12, 24, () => { profile.UpdateCrc(); return profile.GetBytes(); },
            Ddr5AdvancedModel.XmpProfileSize,
            data =>
            {
                var imported = Ddr5XmpProfile.FromBytes(profileNo, data);
                if (!imported.CheckCrcValidity()) { MessageBox.Show(this, "XMP Profile CRC 无效", "导入失败"); return; }
                _ddr5!.SetXmpProfile(profileNo, imported);
                if (profileNo is >= 1 and <= 3) SetDdr5ProfileName(profileNo, "Imported");
            },
            () => XmpProfileService.ValidateDdr5Xmp(profile), $"XMP3_P{profileNo}");

        if (profileNo is >= 1 and <= 3)
        {
            var txtName = new TextBox { Left = 90, Top = 52, Width = 200, Text = GetDdr5ProfileName(profileNo) };
            txtName.TextChanged += (_, _) => SetDdr5ProfileName(profileNo, txtName.Text);
            grp.Controls.AddRange([new Label { Text = "Profile 名称:", Left = 12, Top = 56 }, txtName]);
        }

        AddSpeedBinCombo(grp, 12, 82, true, bin => SpeedBinService.ApplyToDdr5Xmp(profile, bin));

        AddNum(grp, "Min Cycle (ps):", 90, 110, 65535, profile.MinCycleTime, v => profile.MinCycleTime = (int)v);
        AddNum(grp, "VDD:", 280, 110, 300, profile.Vdd, v => profile.Vdd = (int)v);
        AddNum(grp, "VDDQ:", 400, 110, 300, profile.Vddq, v => profile.Vddq = (int)v);
        AddNum(grp, "VPP:", 520, 110, 300, profile.Vpp, v => profile.Vpp = (int)v);
        AddNum(grp, "VMEM:", 640, 110, 300, profile.Vmemctrl, v => profile.Vmemctrl = (int)v);

        var cmbCr = new ComboBox { Left = 90, Top = 142, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbCr.Items.AddRange(["Undefined", "1N", "2N", "3N"]);
        cmbCr.SelectedIndex = Math.Clamp(profile.CommandRate, 0, 3);
        cmbCr.SelectedIndexChanged += (_, _) => profile.CommandRate = cmbCr.SelectedIndex;
        grp.Controls.AddRange([new Label { Text = "命令速率:", Left = 12, Top = 146 }, cmbCr]);

        var chkDmb = new CheckBox { Text = "Intel Dynamic Memory Boost", Left = 220, Top = 144, AutoSize = true, Checked = profile.IntelDynamicMemoryBoost };
        chkDmb.CheckedChanged += (_, _) => profile.IntelDynamicMemoryBoost = chkDmb.Checked;
        var chkRtoc = new CheckBox { Text = "Realtime Memory Frequency OC", Left = 420, Top = 144, AutoSize = true, Checked = profile.RealtimeMemoryFrequencyOc };
        chkRtoc.CheckedChanged += (_, _) => profile.RealtimeMemoryFrequencyOc = chkRtoc.Checked;
        grp.Controls.AddRange([chkDmb, chkRtoc]);

        AddXmpClRow(grp, profile, 178);
        AddTimingRowDdr5(grp, profile, 240);
        if (profile.IsEmpty) { profile.LoadSample(); _ddr5!.EnableXmp(); }

        page.Controls.Add(grp);
        return page;
    }

    private TabPage BuildExpoTab(string title, int profileNo, ExpoProfile profile)
    {
        var page = new TabPage(title) { AutoScroll = true };
        var grp = new GroupBox { Text = title, Width = 920, Height = 400, Top = 8, Left = 8 };

        var chkEnable = new CheckBox
        {
            Text = "启用 EXPO Profile",
            Left = 12, Top = 24, AutoSize = true,
            Checked = profileNo == 1 ? _ddr5!.Expo1Enabled : _ddr5!.Expo2Enabled,
        };
        chkEnable.CheckedChanged += (_, _) =>
        {
            if (!_ddr5!.ExpoFound) _ddr5.EnableExpo();
            if (profileNo == 1) _ddr5.Expo1Enabled = chkEnable.Checked;
            else _ddr5.Expo2Enabled = chkEnable.Checked;
        };
        grp.Controls.Add(chkEnable);

        AddProfileIoButtons(grp, 200, 20, () => profile.GetBytes(), ExpoProfile.ExpoProfileSize,
            data => { profile.Load(data); _ddr5!.EnableExpo(); },
            () => !profile.IsEmpty, $"EXPO_P{profileNo}");

        AddSpeedBinCombo(grp, 12, 52, true, bin => SpeedBinService.ApplyToExpo(profile, bin));
        AddNum(grp, "Min Cycle (ps):", 90, 80, 65535, profile.MinCycleTime, v => profile.MinCycleTime = (int)v);
        AddNum(grp, "VDD:", 280, 80, 300, profile.Vdd, v => profile.Vdd = (int)v);
        AddNum(grp, "VDDQ:", 400, 80, 300, profile.Vddq, v => profile.Vddq = (int)v);
        AddNum(grp, "VPP:", 520, 80, 300, profile.Vpp, v => profile.Vpp = (int)v);
        AddExpoTimingsFull(grp, profile, 118);
        if (profile.IsEmpty) { profile.LoadSample(); _ddr5!.EnableExpo(); }
        page.Controls.Add(grp);
        return page;
    }

    private TabPage BuildMiscTab()
    {
        var page = new TabPage("杂项 (Misc)") { AutoScroll = true };
        var panel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        int y = 8;

        var grpPhys = new GroupBox { Text = "物理特性", Width = 920, Height = 60, Left = 8, Top = y };
        var cmbFf = new ComboBox { Left = 100, Top = 22, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbFf.Items.AddRange(_isDdr5 ? Ddr5AdvancedModel.FormFactors : Ddr4AdvancedModel.FormFactors);
        if (_isDdr5) cmbFf.SelectedItem = _ddr5!.FormFactor; else cmbFf.SelectedItem = _ddr4!.FormFactor;
        cmbFf.SelectedIndexChanged += (_, _) =>
        {
            if (_isDdr5) _ddr5!.FormFactor = cmbFf.SelectedItem?.ToString() ?? "UDIMM";
            else _ddr4!.FormFactor = cmbFf.SelectedItem?.ToString() ?? "UDIMM";
        };
        grpPhys.Controls.AddRange([new Label { Text = "Form Factor:", Left = 12, Top = 26 }, cmbFf]);
        panel.Controls.Add(grpPhys);
        y += 68;

        var grpInfo = new GroupBox { Text = "模组信息", Width = 920, Height = 130, Left = 8, Top = y };
        int year = _isDdr5 ? _ddr5!.ManufacturingYear : _ddr4!.ManufacturingYear;
        int week = _isDdr5 ? _ddr5!.ManufacturingWeek : _ddr4!.ManufacturingWeek;
        string pn = _isDdr5 ? _ddr5!.PartNumber : _ddr4!.PartNumber;
        AddNum(grpInfo, "制造年份:", 12, 24, 99, year, v => { if (_isDdr5) _ddr5!.ManufacturingYear = (int)v; else _ddr4!.ManufacturingYear = (int)v; });
        AddNum(grpInfo, "制造周数:", 200, 24, 52, week, v => { if (_isDdr5) _ddr5!.ManufacturingWeek = (int)v; else _ddr4!.ManufacturingWeek = (int)v; });
        var lblPn = new Label { Text = "料号:", Left = 12, Top = 58 };
        var txtPn = new TextBox { Left = 100, Top = 54, Width = 400, Text = pn };
        txtPn.TextChanged += (_, _) => { if (_isDdr5) _ddr5!.PartNumber = txtPn.Text; else _ddr4!.PartNumber = txtPn.Text; };
        if (_isDdr5)
        {
            var chkHs = new CheckBox { Text = "安装散热片 (Heat Spreader)", Left = 12, Top = 88, Checked = _ddr5!.HeatSpreader, AutoSize = true };
            chkHs.CheckedChanged += (_, _) => _ddr5!.HeatSpreader = chkHs.Checked;
            grpInfo.Controls.Add(chkHs);
        }
        grpInfo.Controls.AddRange([lblPn, txtPn]);
        panel.Controls.Add(grpInfo);
        y += 138;

        if (_isDdr5)
        {
            var grpOrg = new GroupBox { Text = "模组组织", Width = 920, Height = 60, Left = 8, Top = y };
            AddNum(grpOrg, "Bank Groups:", 12, 22, 8, _ddr5!.BankGroups, v => _ddr5.BankGroups = (int)v);
            AddNum(grpOrg, "Device Width:", 220, 22, 32, _ddr5.DeviceWidth, v => _ddr5.DeviceWidth = (int)v);
            panel.Controls.Add(grpOrg);
            y += 68;
        }
        else
        {
            var grpDen = new GroupBox { Text = "密度信息 (只读)", Width = 920, Height = 60, Left = 8, Top = y };
            grpDen.Controls.Add(new Label { Text = $"Density: {_ddr4!.DensityLabel}  |  Bank Groups: {_ddr4.BankGroups}  |  Device Width: {_ddr4.DeviceWidth}", Left = 12, Top = 24, AutoSize = true });
            panel.Controls.Add(grpDen);
            y += 68;
        }

        var grpCopy = new GroupBox { Text = "复制 XMP Profile", Width = 920, Height = 60, Left = 8, Top = y };
        var numSrc = new NumericUpDown { Left = 70, Top = 22, Width = 50, Minimum = 1, Maximum = _isDdr5 ? 5 : 2, Value = 1 };
        var numTgt = new NumericUpDown { Left = 180, Top = 22, Width = 50, Minimum = 1, Maximum = _isDdr5 ? 5 : 2, Value = 2 };
        var btnCopy = new Button { Text = "复制 (Copy)", Left = 260, Top = 20, Width = 90 };
        btnCopy.Click += (_, _) =>
        {
            bool ok = _isDdr5
                ? _ddr5!.CopyXmpProfile((int)numSrc.Value, (int)numTgt.Value)
                : _ddr4!.CopyXmpProfile((int)numSrc.Value, (int)numTgt.Value);
            MessageBox.Show(this, ok ? "XMP Profile 复制成功" : "复制失败", ok ? "成功" : "失败",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        };
        grpCopy.Controls.AddRange([
            new Label { Text = "源:", Left = 12, Top = 26 }, numSrc,
            new Label { Text = "目标:", Left = 130, Top = 26 }, numTgt, btnCopy,
        ]);
        panel.Controls.Add(grpCopy);
        y += 68;

        var grpIe = new GroupBox { Text = "导入/导出 XMP Profile", Width = 920, Height = 60, Left = 8, Top = y };
        var numIe = new NumericUpDown { Left = 70, Top = 22, Width = 50, Minimum = 1, Maximum = _isDdr5 ? 5 : 2, Value = 1 };
        var btnImport = new Button { Text = "导入", Left = 140, Top = 20, Width = 70 };
        var btnExport = new Button { Text = "导出", Left = 220, Top = 20, Width = 70 };
        btnImport.Click += (_, _) =>
        {
            int no = (int)numIe.Value;
            int size = _isDdr5 ? Ddr5AdvancedModel.XmpProfileSize : Ddr4AdvancedModel.XmpProfileSize;
            var data = XmpProfileService.ImportProfile(this, size);
            if (data == null) return;
            if (_isDdr5)
            {
                var p = Ddr5XmpProfile.FromBytes(no, data);
                if (!p.CheckCrcValidity()) { MessageBox.Show(this, "CRC 无效"); return; }
                _ddr5!.SetXmpProfile(no, p);
                if (no is >= 1 and <= 3) SetDdr5ProfileName(no, "Imported");
            }
            else _ddr4!.SetXmpProfile(no, Ddr4XmpProfile.FromBytes(no, data));
            MessageBox.Show(this, "导入成功");
        };
        btnExport.Click += (_, _) =>
        {
            int no = (int)numIe.Value;
            byte[] bytes;
            string name;
            if (_isDdr5)
            {
                var p = _ddr5!.GetXmpProfile(no);
                if (p == null || !XmpProfileService.ValidateDdr5Xmp(p)) { MessageBox.Show(this, "Profile 无效或为空"); return; }
                p.UpdateCrc(); bytes = p.GetBytes(); name = $"XMP3_P{no}";
            }
            else
            {
                var p = _ddr4!.GetXmpProfile(no);
                if (p == null || !XmpProfileService.ValidateDdr4Xmp(p)) { MessageBox.Show(this, "Profile 无效或为空"); return; }
                bytes = p.GetBytes(); name = $"XMP2_P{no}";
            }
            XmpProfileService.ExportProfile(this, bytes, name);
        };
        grpIe.Controls.AddRange([
            new Label { Text = "Profile:", Left = 12, Top = 26 }, numIe, btnImport, btnExport,
        ]);
        panel.Controls.Add(grpIe);

        page.Controls.Add(panel);
        return page;
    }

    private void AddProfileIoButtons(GroupBox grp, int x, int y, Func<byte[]> getBytes, int size, Action<byte[]> import, Func<bool> canExport, string exportName)
    {
        var btnImport = new Button { Text = "导入", Left = x, Top = y, Width = 60 };
        var btnExport = new Button { Text = "导出", Left = x + 70, Top = y, Width = 60 };
        btnImport.Click += (_, _) =>
        {
            var data = XmpProfileService.ImportProfile(this, size);
            if (data != null) import(data);
        };
        btnExport.Click += (_, _) =>
        {
            if (!canExport()) { MessageBox.Show(this, "Profile 无效或为空"); return; }
            XmpProfileService.ExportProfile(this, getBytes(), exportName);
        };
        grp.Controls.AddRange([btnImport, btnExport]);
    }

    private string GetDdr5ProfileName(int no) => no switch
    {
        1 => _ddr5!.XmpProfile1Name, 2 => _ddr5!.XmpProfile2Name, 3 => _ddr5!.XmpProfile3Name, _ => "",
    };

    private void SetDdr5ProfileName(int no, string name)
    {
        if (no == 1) _ddr5!.XmpProfile1Name = name;
        else if (no == 2) _ddr5!.XmpProfile2Name = name;
        else if (no == 3) _ddr5!.XmpProfile3Name = name;
    }

    private void AddXmpClRow(GroupBox grp, Ddr5XmpProfile profile, int top)
    {
        grp.Controls.Add(new Label { Text = "CAS Latency:", Left = 12, Top = top + 4 });
        int x = 90;
        for (int cl = 20; cl <= 56; cl += 2)
        {
            var cb = new CheckBox
            {
                Text = cl.ToString(), Width = 42, Left = x, Top = top,
                Checked = profile.IsClSupported(cl),
            };
            int c = cl;
            cb.CheckedChanged += (_, _) => profile.SetClSupported(c, cb.Checked);
            grp.Controls.Add(cb);
            x += 44;
            if (x > 850) { x = 90; top += 26; }
        }
    }

    private void AddExpoTimingsFull(GroupBox grp, ExpoProfile p, int top)
    {
        (string label, Func<int> get, Action<int> set)[] items =
        [
            ("tAA", () => p.TAA, v => p.TAA = v), ("tRCD", () => p.TRCD, v => p.TRCD = v),
            ("tRP", () => p.TRP, v => p.TRP = v), ("tRAS", () => p.TRAS, v => p.TRAS = v),
            ("tRC", () => p.TRC, v => p.TRC = v), ("tWR", () => p.TWR, v => p.TWR = v),
            ("tRFC1", () => p.TRFC1, v => p.TRFC1 = v), ("tRRD_L", () => p.TRRD_L, v => p.TRRD_L = v),
            ("tFAW", () => p.TFAW, v => p.TFAW = v),
        ];
        for (int i = 0; i < items.Length; i++)
            AddNum(grp, items[i].label + " (ps):", 12 + (i % 3) * 290, top + (i / 3) * 34, 65535, items[i].get(), v => items[i].set((int)v));
    }

    private ComboBox AddSpeedBinCombo(Control parent, int x, int y, bool ddr5, Action<string> onApply)
    {
        parent.Controls.Add(new Label { Text = "Speed Bin:", Left = x, Top = y + 4 });
        var cmb = new ComboBox { Left = x + 72, Top = y, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        cmb.Items.Add("");
        foreach (var n in SpeedBinService.GetBinNames(ddr5)) cmb.Items.Add(n);
        var btn = new Button { Text = "Apply", Left = x + 240, Top = y - 2, Width = 60 };
        btn.Click += (_, _) => { if (cmb.SelectedItem is string s && !string.IsNullOrEmpty(s)) onApply(s); };
        parent.Controls.AddRange([cmb, btn]);
        return cmb;
    }

    private void AddTimingFields(GroupBox grp, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            int row = i / 2, col = i % 2;
            int left = 12 + col * 440;
            int top = 24 + row * 32;
            string name = names[i];
            grp.Controls.Add(new Label { Text = name + ":", Left = left, Top = top + 4, Width = 100 });
            int val = GetJedecTimingPs(name);
            var num = new NumericUpDown { Minimum = 0, Maximum = 65535, Value = Math.Max(0, val), Width = 90, Left = left + 105, Top = top, Tag = name };
            num.ValueChanged += (_, _) => SetJedecTimingPs(name, (int)num.Value);
            grp.Controls.Add(num);
        }
    }

    private void AddTimingRowDdr4(GroupBox grp, Ddr4XmpProfile p, int top)
    {
        (string name, Func<int> get, Action<int> set)[] items =
        [
            ("CL", () => p.ClTicks, v => p.ClTicks = v),
            ("RCD", () => p.RcdTicks, v => p.RcdTicks = v),
            ("RP", () => p.RpTicks, v => p.RpTicks = v),
            ("RAS", () => p.RasTicks, v => p.RasTicks = v),
            ("RC", () => p.RcTicks, v => p.RcTicks = v),
        ];
        for (int i = 0; i < items.Length; i++)
            AddNum(grp, $"t{items[i].name} ticks:", 12 + i * 170, top, 65535, items[i].get(), v => items[i].set((int)v));
    }

    private void AddTimingRowDdr5(GroupBox grp, Ddr5XmpProfile p, int top)
    {
        (string label, Func<int> get, Action<int> set)[] items =
        [
            ("tAA", () => p.TAA, v => p.TAA = v),
            ("tRCD", () => p.TRCD, v => p.TRCD = v),
            ("tRP", () => p.TRP, v => p.TRP = v),
            ("tRAS", () => p.TRAS, v => p.TRAS = v),
            ("tRC", () => p.TRC, v => p.TRC = v),
            ("tWR", () => p.TWR, v => p.TWR = v),
        ];
        for (int i = 0; i < items.Length; i++)
            AddNum(grp, items[i].label + " (ps):", 12 + (i % 3) * 290, top + (i / 3) * 34, 65535, items[i].get(), v => items[i].set((int)v));
    }

    private NumericUpDown AddNum(Control parent, string label, int x, int y, int max, int value, Action<decimal>? onChange = null)
    {
        parent.Controls.Add(new Label { Text = label, Left = x, Top = y + 4, AutoSize = true });
        var num = new NumericUpDown { Minimum = 0, Maximum = max, Value = Math.Clamp(value, 0, max), Left = x + 110, Top = y, Width = 90 };
        if (onChange != null) num.ValueChanged += (_, _) => onChange(num.Value);
        parent.Controls.Add(num);
        return num;
    }

    private void LoadAllTabs()
    {
        foreach (var kv in _clChecks)
        {
            bool supported = _isDdr5 ? _ddr5!.IsClSupported(kv.Key) : _ddr4!.IsClSupported(kv.Key);
            kv.Value.Checked = supported;
        }
    }

    private int GetJedecTimingPs(string name)
    {
        if (_isDdr5)
            return name switch
            {
                "tAA" => _ddr5!.TAA,
                "tRCD" => _ddr5.TRCD,
                "tRP" => _ddr5.TRP,
                "tRAS" => _ddr5.TRAS,
                "tRC" => _ddr5.TRC,
                "tWR" => _ddr5.TWR,
                "tRFC1_slr" => _ddr5.TRFC1Slr,
                "tRFC2_slr" => _ddr5.TRFC2Slr,
                "tRFCsb_slr" => _ddr5.TRFCsbSlr,
                "tRRD_L" => _ddr5.TRRD_L,
                "tCCD_L" => _ddr5.TCCD_L,
                "tFAW" => _ddr5.TFAW,
                "tCCD_L_WTR" => _ddr5.TCCD_L_WTR,
                "tCCD_S_WTR" => _ddr5.TCCD_S_WTR,
                "tRTP" => _ddr5.TRTP,
                _ => 0,
            };
        return _ddr4!.GetTimingPs(name);
    }

    private void SetJedecTimingPs(string name, int ps)
    {
        if (_isDdr5) _ddr5!.SetTimingPs(name, ps);
        else _ddr4!.SetTimingTicks(name, SpdUtils.NsToTicksDdr4(ps / 1000.0));
    }

    private void ApplyAndClose()
    {
        ResultData = _isDdr5 ? _ddr5!.GetBytes() : _ddr4!.GetBytes();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string GetFreqLabel(decimal minCyclePs)
    {
        int ps = (int)minCyclePs;
        if (ps <= 0) return "";
        return $"{SpdUtils.FrequencyMhzFromMinCyclePs(ps):F0} MHz / {SpdUtils.MtFromMinCyclePs(ps):F0} MT/s";
    }

    private static byte[] Pad(byte[] data, int size)
    {
        var buf = new byte[size];
        Array.Copy(data, buf, Math.Min(data.Length, size));
        return buf;
    }
}

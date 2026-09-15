using SpdEditor.Core;

namespace SpdEditor;

/// <summary>分页展示并可编辑写回当前 BIN 的 SPD / XMP / EXPO 配置（布局对齐 ddrxmpeditor-pro）。</summary>
public sealed class XmpInfoForm : Form
{
    private byte[] _working;
    private readonly string _spdSummary;
    private readonly SpdMemoryType _memType;
    private readonly List<Ddr5ProfileView> _ddr5Profiles;
    private readonly List<Ddr4ProfileView> _ddr4Profiles;
    private readonly List<ProfilePageBinder> _ddr5Binders = [];
    private readonly List<Ddr4ProfilePageBinder> _ddr4Binders = [];
    private readonly CheckBox[] _topChecks = new CheckBox[5];
    private readonly TabControl _tabs = new();

    public bool Applied { get; private set; }
    public byte[] ResultData => _working;

    public XmpInfoForm(byte[] spdData, string? spdSummary = null)
    {
        _working = (byte[])spdData.Clone();
        _spdSummary = spdSummary ?? "";
        _memType = SpdParser.ResolveType(_working);
        _ddr5Profiles = _memType == SpdMemoryType.Ddr5
            ? Ddr5ProfileLoader.LoadAll(_working).ToList()
            : [];
        _ddr4Profiles = _memType == SpdMemoryType.Ddr4
            ? Ddr4ProfileLoader.LoadAll(_working).ToList()
            : [];

        Text = "XMP / SPD 配置信息";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1020, 760);
        ClientSize = new Size(1040, 800);
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;

        BuildUi();
    }

    private static string ShortTabTitle(string title) => title switch
    {
        "用户预设XMP1" => "用户1",
        "用户预设XMP2" => "用户2",
        _ => title,
    };

    private void BuildUi()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(12, 8, 12, 0),
            WrapContents = false,
        };

        string[] topLabels = ["XMP 1", "XMP 2", "XMP 3", "用户预设1", "用户预设2"];
        for (int i = 0; i < 5; i++)
        {
            var chk = new CheckBox
            {
                Text = topLabels[i],
                AutoSize = true,
                Enabled = false,
                Visible = true,
                Margin = new Padding(2, 2, 16, 2),
                Font = new Font("Microsoft YaHei UI", 9F),
            };
            _topChecks[i] = chk;
            top.Controls.Add(chk);
        }

        if (_memType == SpdMemoryType.Ddr4)
        {
            for (int i = 2; i < 5; i++)
                _topChecks[i].Visible = false;

            bool hasXmp = Ddr4ProfileLoader.HasXmp(_working);
            for (int i = 0; i < 2; i++)
            {
                _topChecks[i].Enabled = hasXmp && i + 1 < _ddr4Profiles.Count && _ddr4Profiles[i + 1].Writable;
                if (i + 1 < _ddr4Profiles.Count)
                    _topChecks[i].Checked = _ddr4Profiles[i + 1].Enabled;
            }
        }
        else
        {
            bool hasXmp = Ddr5ProfileLoader.HasXmp(_working);
            for (int i = 0; i < 3; i++)
            {
                _topChecks[i].Enabled = hasXmp && i + 1 < _ddr5Profiles.Count && _ddr5Profiles[i + 1].Writable;
                if (i + 1 < _ddr5Profiles.Count)
                    _topChecks[i].Checked = _ddr5Profiles[i + 1].Enabled;
            }
            for (int i = 3; i < 5; i++)
            {
                if (i + 1 < _ddr5Profiles.Count)
                    _topChecks[i].Checked = _ddr5Profiles[i + 1].Present && _ddr5Profiles[i + 1].TckPs > 0;
            }
        }

        var lblSpd = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(14, 4, 12, 2),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            Text = string.IsNullOrWhiteSpace(_spdSummary)
                ? "当前导入的SPD:"
                : $"当前导入的SPD: {_spdSummary}",
        };

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(10, 6);
        _tabs.Font = new Font("Microsoft YaHei UI", 9F);
        _tabs.SizeMode = TabSizeMode.Normal;
        _tabs.ItemSize = new Size(78, 26);

        if (_memType == SpdMemoryType.Ddr4 && _ddr4Profiles.Count > 0)
        {
            foreach (var p in _ddr4Profiles)
            {
                var page = BuildDdr4ProfileTab(p);
                page.Text = ShortTabTitle(p.TabTitle);
                _tabs.TabPages.Add(page);
            }
            _tabs.TabPages.Add(BuildMiscTab());
        }
        else if (_memType == SpdMemoryType.Ddr5 && _ddr5Profiles.Count > 0)
        {
            foreach (var p in _ddr5Profiles)
            {
                var page = BuildDdr5ProfileTab(p);
                page.Text = ShortTabTitle(p.TabTitle);
                _tabs.TabPages.Add(page);
            }
            _tabs.TabPages.Add(BuildMiscTab());
        }
        else
        {
            var empty = new TabPage("提示");
            empty.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "当前 BIN 不是 DDR4/DDR5，或数据过短，无法按此窗体编辑。",
            });
            _tabs.TabPages.Add(empty);
        }

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 10, 12, 10) };
        var btnSave = new Button
        {
            Text = "保存到BIN",
            Size = new Size(108, 30),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(88, 30),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        btnSave.Click += (_, _) => SaveToWorking();
        void LayoutBottom()
        {
            btnCancel.Location = new Point(bottom.ClientSize.Width - 100, 10);
            btnSave.Location = new Point(bottom.ClientSize.Width - 220, 10);
        }
        bottom.Resize += (_, _) => LayoutBottom();
        bottom.Controls.AddRange([btnSave, btnCancel]);
        LayoutBottom();
        CancelButton = btnCancel;

        Controls.Add(_tabs);
        Controls.Add(lblSpd);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private void SaveToWorking()
    {
        if (_memType == SpdMemoryType.Ddr4)
            SaveDdr4();
        else if (_memType == SpdMemoryType.Ddr5)
            SaveDdr5();
        else
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void SaveDdr5()
    {
        if (_ddr5Profiles.Count == 0)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        foreach (var binder in _ddr5Binders)
        {
            if (binder.Meta.Kind == Ddr5ProfileKind.Jedec)
                continue;

            var edit = binder.Collect();
            if (!Ddr5ProfileLoader.TryWriteProfile(_working, binder.Meta, edit, out string err))
            {
                MessageBox.Show(this, err, "写回失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (Ddr5ProfileLoader.HasXmp(_working))
            Ddr5ProfileLoader.SetXmpEnableBits(_working, _topChecks[0].Checked, _topChecks[1].Checked, _topChecks[2].Checked);

        byte[] data = _working;
        if (!SpdEditorLogic.FinalizeForFlash(ref data, SpdMemoryType.Ddr5, out var compliance))
        {
            MessageBox.Show(this, $"CRC 完善失败:\n{compliance.Summary}", "写回失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _working = data;
        Applied = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SaveDdr4()
    {
        if (_ddr4Profiles.Count == 0)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        foreach (var binder in _ddr4Binders)
        {
            // SPD(JEDEC) 页只读，不写回
            if (binder.Meta.Kind == Ddr4ProfileKind.Jedec)
                continue;

            var edit = binder.Collect();
            if (!Ddr4ProfileLoader.TryWriteProfile(_working, binder.Meta, edit, out string err))
            {
                MessageBox.Show(this, err, "写回失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (Ddr4ProfileLoader.HasXmp(_working) || _ddr4Binders.Any(b => b.Meta.Kind == Ddr4ProfileKind.Xmp && b.Meta.Writable))
            Ddr4ProfileLoader.SetXmpEnableBits(_working, _topChecks[0].Checked, _topChecks[1].Checked);

        byte[] data = _working;
        if (!SpdEditorLogic.FinalizeForFlash(ref data, SpdMemoryType.Ddr4, out var compliance))
        {
            MessageBox.Show(this, $"CRC 完善失败:\n{compliance.Summary}", "写回失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _working = data;
        Applied = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private TabPage BuildDdr5ProfileTab(Ddr5ProfileView p)
    {
        var page = new TabPage(p.TabTitle) { Padding = new Padding(8) };
        if (!p.Writable)
        {
            page.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = p.Kind == Ddr5ProfileKind.Expo
                    ? "此 EXPO 配置块不存在（BIN 中无 EXPO 签名）。"
                    : "此配置块不存在或未被写入。",
            });
            return page;
        }

        var binder = new ProfilePageBinder(p);
        _ddr5Binders.Add(binder);
        page.Controls.Add(binder.BuildPanel());
        return page;
    }

    private TabPage BuildDdr4ProfileTab(Ddr4ProfileView p)
    {
        var page = new TabPage(p.TabTitle) { Padding = new Padding(8) };
        if (!p.Present && p.Kind == Ddr4ProfileKind.Xmp && !p.Writable)
        {
            page.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "此配置块不存在或 BIN 中无 XMP 2.0 签名。",
            });
            return page;
        }

        if (p.Kind == Ddr4ProfileKind.Xmp && !p.Writable)
        {
            page.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "此配置块不存在或未被写入。",
            });
            return page;
        }

        var binder = new Ddr4ProfilePageBinder(p);
        _ddr4Binders.Add(binder);
        page.Controls.Add(binder.BuildPanel());
        return page;
    }

    private TabPage BuildMiscTab()
    {
        var page = new TabPage("杂项") { Padding = new Padding(8) };
        page.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F),
            Text = SpdXmpReader.FormatReport(_working) + "\r\n\r\n修改后点「保存到BIN」写回内存映像并自动重算 CRC。",
        });
        return page;
    }

    #region DDR4 binder

    private sealed class Ddr4ProfilePageBinder
    {
        public Ddr4ProfileView Meta { get; }

        private ComboBox _cboSpeedBin = null!;
        private NumericUpDown _nudTck = null!;
        private Label _lblMhz = null!;
        private Label _lblMt = null!;
        private NumericUpDown? _nudVdd;
        private readonly Dictionary<int, CheckBox> _cas = [];
        private readonly string[] _timingNames;
        private readonly NumericUpDown[] _timingPs;
        private readonly NumericUpDown[] _timingNck;
        private bool _syncing;

        private static readonly Font UiFont = new("Microsoft YaHei UI", 9F);
        private static readonly Font HeaderFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);
        private static readonly Font HintFont = new("Microsoft YaHei UI", 8.5F);

        public Ddr4ProfilePageBinder(Ddr4ProfileView meta)
        {
            Meta = meta;
            if (meta.Kind == Ddr4ProfileKind.Jedec)
            {
                _timingNames =
                [
                    "tAA", "tRCD", "tRP", "tRAS", "tRC", "tWR",
                    "tRFC1", "tRFC2", "tRFC4",
                    "tRRD_S", "tRRD_L", "tCCD_L", "tFAW", "tWTR_S", "tWTR_L",
                ];
            }
            else
            {
                _timingNames =
                [
                    "tAA", "tRCD", "tRP", "tRAS", "tRC", "tWR",
                    "tRFC1", "tRFC2", "tRFC4",
                    "tRRD_S", "tRRD_L", "tFAW",
                ];
            }
            _timingPs = new NumericUpDown[_timingNames.Length];
            _timingNck = new NumericUpDown[_timingNames.Length];
        }

        public Panel BuildPanel()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4),
                Font = UiFont,
            };
            bool edit = Meta.Writable && Meta.Kind != Ddr4ProfileKind.Jedec;
            const int contentW = 980;
            int y = 4;

            int freqH = 78;
            var grpFreq = new GroupBox
            {
                Text = "频率 (Frequency) / Speed Bin",
                Location = new Point(4, y),
                Size = new Size(contentW, freqH),
                Font = UiFont,
            };
            BuildFrequency(grpFreq, edit);
            root.Controls.Add(grpFreq);
            y += freqH + 6;

            if (Meta.Kind == Ddr4ProfileKind.Xmp)
            {
                var grpVolt = new GroupBox
                {
                    Text = "电压 (Voltage) - DDR4",
                    Location = new Point(4, y),
                    Size = new Size(contentW, 54),
                    Font = UiFont,
                };
                BuildVoltage(grpVolt, edit);
                root.Controls.Add(grpVolt);
                y += 60;
            }

            var grpCas = new GroupBox
            {
                Text = "支持的 CAS Latency (DDR4)",
                Location = new Point(4, y),
                Size = new Size(contentW, 110),
                Font = UiFont,
            };
            BuildCas(grpCas, edit);
            root.Controls.Add(grpCas);
            y += 116;

            int timingRows = (_timingNames.Length + 1) / 2;
            int timingH = 44 + timingRows * 30 + 16;
            var grpTiming = new GroupBox
            {
                Text = "时序参数 (Timings) - DDR4",
                Location = new Point(4, y),
                Size = new Size(contentW, timingH),
                Font = UiFont,
            };
            BuildTiming(grpTiming, edit);
            root.Controls.Add(grpTiming);

            ApplyToUi(FromMeta());
            return root;
        }

        private void BuildFrequency(GroupBox grp, bool edit)
        {
            grp.Controls.Add(Lbl("Speed Bin:", 16, 24));
            _cboSpeedBin = new ComboBox
            {
                Location = new Point(100, 20),
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = edit,
                Font = UiFont,
            };
            _cboSpeedBin.Items.Add("");
            foreach (var name in Ddr4SpeedBins.Names.OrderBy(n => n, StringComparer.Ordinal))
                _cboSpeedBin.Items.Add(name);
            _cboSpeedBin.SelectedIndex = 0;

            var btnApply = new Button
            {
                Text = "Apply",
                Location = new Point(270, 19),
                Size = new Size(64, 26),
                Enabled = edit,
                Font = UiFont,
            };
            btnApply.Click += (_, _) => ApplySpeedBin();

            var hint = new Label
            {
                Text = "选择 Speed Bin 后点击 Apply 自动填充参数",
                Location = new Point(344, 24),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = HintFont,
            };

            grp.Controls.Add(Lbl("Min Cycle Time (ps):", 16, 52));
            int tckPs = Meta.TckPs > 0 ? Meta.TckPs : 1;
            _nudTck = MakeNud(tckPs, 1, 65535, edit, 90);
            _nudTck.Location = new Point(160, 48);
            _nudTck.ThousandsSeparator = false;
            _nudTck.ValueChanged += (_, _) => OnTckChanged();

            _lblMhz = new Label { Location = new Point(270, 52), Size = new Size(140, 22), Font = UiFont };
            _lblMt = new Label { Location = new Point(420, 52), Size = new Size(140, 22), Font = UiFont };

            grp.Controls.AddRange([_cboSpeedBin, btnApply, hint, _nudTck, _lblMhz, _lblMt]);
        }

        private void BuildVoltage(GroupBox grp, bool edit)
        {
            int vdd = Meta.VddCv is >= 110 and <= 300 ? Meta.VddCv : 120;
            _nudVdd = MakeNud(vdd, 110, 300, edit, 64);
            _nudVdd.Location = new Point(52, 22);
            _nudVdd.ThousandsSeparator = false;
            grp.Controls.AddRange([Lbl("VDD:", 16, 25), _nudVdd]);
        }

        private void BuildCas(GroupBox grp, bool edit)
        {
            const int cols = 12;
            const int cellW = 76;
            const int cellH = 20;
            const int originX = 14;
            const int originY = 22;

            int idx = 0;
            for (int cl = 7; cl <= 36; cl++)
            {
                int row = idx / cols;
                int col = idx % cols;
                idx++;
                bool canStore = Meta.Kind != Ddr4ProfileKind.Xmp || cl <= 30;
                var chk = new CheckBox
                {
                    Text = cl.ToString(),
                    AutoSize = true,
                    Location = new Point(originX + col * cellW, originY + row * cellH),
                    Enabled = edit && canStore,
                    Checked = Meta.SupportedCas.Contains(cl),
                    Font = SmallFont,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                };
                _cas[cl] = chk;
                grp.Controls.Add(chk);
            }
        }

        private void BuildTiming(GroupBox grp, bool edit)
        {
            // 与 DDR5 一致：参数名 | 值(ps) | Ticks(nCK)
            const int row0 = 44;
            const int rowH = 30;
            const int lName = 12, lPs = 110, lTick = 230;
            const int rName = 360, rPs = 470, rTick = 600;

            grp.Controls.Add(Header("参数名", lName, 18));
            grp.Controls.Add(Header("值 (ps)", lPs, 18));
            grp.Controls.Add(Header("Ticks", lTick, 18));
            grp.Controls.Add(Header("参数名", rName, 18));
            grp.Controls.Add(Header("值 (ps)", rPs, 18));
            grp.Controls.Add(Header("Ticks", rTick, 18));

            int leftCount = (_timingNames.Length + 1) / 2;
            for (int i = 0; i < _timingNames.Length; i++)
            {
                bool isLeft = i < leftCount;
                int row = isLeft ? i : i - leftCount;
                int yy = row0 + row * rowH;
                int nameX = isLeft ? lName : rName;
                int psX = isLeft ? lPs : rPs;
                int tickX = isLeft ? lTick : rTick;

                grp.Controls.Add(Lbl(_timingNames[i] + ":", nameX, yy + 4));
                _timingPs[i] = MakeNud(0, 0, 1_000_000, edit, 96);
                _timingPs[i].Location = new Point(psX, yy);
                _timingNck[i] = MakeNud(0, 0, 65535, edit, 64);
                _timingNck[i].Location = new Point(tickX, yy);
                int ti = i;
                _timingPs[i].ValueChanged += (_, _) => OnPsChanged(ti);
                _timingNck[i].ValueChanged += (_, _) => OnNckChanged(ti);
                grp.Controls.AddRange([_timingPs[i], _timingNck[i]]);
            }
        }

        private void ApplySpeedBin()
        {
            string? name = _cboSpeedBin.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请先选择 Speed Bin。", "Speed Bin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Ddr4SpeedBins.TryApply(name, Meta.Kind, out var edit, out string err))
            {
                MessageBox.Show(err, "Speed Bin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyToUi(edit);
        }

        private Ddr4ProfileEdit FromMeta() => new()
        {
            TckMtb = Meta.TckMtb,
            TckFc = Meta.TckFc,
            TckPs = Meta.TckPs > 0 ? Meta.TckPs : SpdUtils.TicksToPsDdr4(Meta.TckMtb) + Meta.TckFc,
            SupportedCas = new HashSet<int>(Meta.SupportedCas),
            VddCv = Meta.VddCv > 0 ? Meta.VddCv : 120,
            ClTicks = Meta.ClTicks,
            ClFc = Meta.ClFc,
            RcdTicks = Meta.RcdTicks,
            RcdFc = Meta.RcdFc,
            RpTicks = Meta.RpTicks,
            RpFc = Meta.RpFc,
            RasTicks = Meta.RasTicks,
            RcTicks = Meta.RcTicks,
            RcFc = Meta.RcFc,
            WrTicks = Meta.WrTicks > 0 ? Meta.WrTicks : SpdUtils.PsToMtbTicksDdr4(15_000),
            Rfc1Ticks = Meta.Rfc1Ticks,
            Rfc2Ticks = Meta.Rfc2Ticks,
            Rfc4Ticks = Meta.Rfc4Ticks,
            FawTicks = Meta.FawTicks,
            RrdsTicks = Meta.RrdsTicks,
            RrdsFc = Meta.RrdsFc,
            RrdlTicks = Meta.RrdlTicks,
            RrdlFc = Meta.RrdlFc,
            CcdlTicks = Meta.CcdlTicks,
            CcdlFc = Meta.CcdlFc,
            WtrsTicks = Meta.WtrsTicks,
            WtrlTicks = Meta.WtrlTicks,
        };

        private static int PsOf(int mtb, int fc) => SpdUtils.TicksToPsDdr4(mtb) + fc;

        private void ApplyToUi(Ddr4ProfileEdit e)
        {
            _syncing = true;
            try
            {
                int tckPs = e.TckPs > 0 ? e.TckPs : PsOf(e.TckMtb, e.TckFc);
                SetNud(_nudTck, tckPs > 0 ? tckPs : 1);
                UpdateFreqLabels((int)_nudTck.Value);

                if (_nudVdd != null) SetNud(_nudVdd, e.VddCv > 0 ? e.VddCv : 120);

                foreach (var (cl, chk) in _cas)
                    chk.Checked = e.SupportedCas.Contains(cl);

                // 统一为 ps 显示（MTB×125 + FTB），Ticks 为当前频率下的 nCK
                int[] psVals;
                if (Meta.Kind == Ddr4ProfileKind.Jedec)
                {
                    psVals =
                    [
                        PsOf(e.ClTicks, e.ClFc), PsOf(e.RcdTicks, e.RcdFc), PsOf(e.RpTicks, e.RpFc),
                        PsOf(e.RasTicks, 0), PsOf(e.RcTicks, e.RcFc), PsOf(e.WrTicks, 0),
                        PsOf(e.Rfc1Ticks, 0), PsOf(e.Rfc2Ticks, 0), PsOf(e.Rfc4Ticks, 0),
                        PsOf(e.RrdsTicks, e.RrdsFc), PsOf(e.RrdlTicks, e.RrdlFc), PsOf(e.CcdlTicks, e.CcdlFc),
                        PsOf(e.FawTicks, 0), PsOf(e.WtrsTicks, 0), PsOf(e.WtrlTicks, 0),
                    ];
                }
                else
                {
                    int wrPs = e.WrTicks > 0 ? PsOf(e.WrTicks, 0) : 15_000;
                    psVals =
                    [
                        PsOf(e.ClTicks, e.ClFc), PsOf(e.RcdTicks, e.RcdFc), PsOf(e.RpTicks, e.RpFc),
                        PsOf(e.RasTicks, 0), PsOf(e.RcTicks, e.RcFc), wrPs,
                        PsOf(e.Rfc1Ticks, 0), PsOf(e.Rfc2Ticks, 0), PsOf(e.Rfc4Ticks, 0),
                        PsOf(e.RrdsTicks, e.RrdsFc), PsOf(e.RrdlTicks, e.RrdlFc), PsOf(e.FawTicks, 0),
                    ];
                }

                int tck = CurrentTckPs();
                for (int i = 0; i < _timingPs.Length && i < psVals.Length; i++)
                {
                    SetNud(_timingPs[i], psVals[i]);
                    SetNud(_timingNck[i], Nck(psVals[i], tck));
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnTckChanged()
        {
            if (_syncing) return;
            UpdateFreqLabels((int)_nudTck.Value);
            RefreshNckFromPs();
        }

        private void OnPsChanged(int index)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                SetNud(_timingNck[index], Nck((int)_timingPs[index].Value, CurrentTckPs()));
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnNckChanged(int index)
        {
            if (_syncing) return;
            int tck = CurrentTckPs();
            if (tck <= 0) return;
            _syncing = true;
            try
            {
                int nck = (int)_timingNck[index].Value;
                SetNud(_timingPs[index], SpdTimingRules.TicksToTimeDdr4(nck, tck));
            }
            finally
            {
                _syncing = false;
            }
        }

        private void RefreshNckFromPs()
        {
            bool was = _syncing;
            _syncing = true;
            try
            {
                int tck = CurrentTckPs();
                for (int i = 0; i < _timingPs.Length; i++)
                    SetNud(_timingNck[i], Nck((int)_timingPs[i].Value, tck));
            }
            finally
            {
                _syncing = was;
            }
        }

        private int CurrentTckPs() => (int)_nudTck.Value;

        private static int Nck(int timePs, int tck) =>
            SpdTimingRules.TimeToTicksDdr4(timePs, tck);

        private void UpdateFreqLabels(int tckPs)
        {
            int mhz = tckPs > 0 ? (int)Math.Round(1_000_000.0 / tckPs) : 0;
            int mt = tckPs > 0 ? (int)Math.Round(SpdUtils.MtFromMinCyclePs(tckPs)) : 0;
            _lblMhz.Text = $"{mhz} MHz";
            _lblMt.Text = $"{mt} MT/s";
        }

        public Ddr4ProfileEdit Collect()
        {
            var cas = new HashSet<int>();
            foreach (var (cl, chk) in _cas)
            {
                if (chk.Checked) cas.Add(cl);
            }

            int tckPs = (int)_nudTck.Value;
            var (tckMtb, tckFc) = SpdTimingRules.FitPsToMtbFtb(tckPs);

            var edit = new Ddr4ProfileEdit
            {
                TckPs = tckPs,
                TckMtb = tckMtb,
                TckFc = tckFc,
                SupportedCas = cas,
                VddCv = (int)(_nudVdd?.Value ?? (Meta.VddCv > 0 ? Meta.VddCv : 120)),
            };

            // UI 存的是 ps，写回前拆成 MTB + FTB
            int[] ps = _timingPs.Select(n => (int)n.Value).ToArray();
            void SetMtb(int i, Action<int, int> assign)
            {
                int p = i < ps.Length ? ps[i] : 0;
                var (mtb, fc) = SpdTimingRules.FitPsToMtbFtb(p);
                assign(mtb, fc);
            }

            SetMtb(0, (m, f) => { edit.ClTicks = m; edit.ClFc = f; });
            SetMtb(1, (m, f) => { edit.RcdTicks = m; edit.RcdFc = f; });
            SetMtb(2, (m, f) => { edit.RpTicks = m; edit.RpFc = f; });
            SetMtb(3, (m, _) => { edit.RasTicks = m; });
            SetMtb(4, (m, f) => { edit.RcTicks = m; edit.RcFc = f; });
            SetMtb(5, (m, _) => { edit.WrTicks = m; });
            SetMtb(6, (m, _) => { edit.Rfc1Ticks = m; });
            SetMtb(7, (m, _) => { edit.Rfc2Ticks = m; });
            SetMtb(8, (m, _) => { edit.Rfc4Ticks = m; });

            if (Meta.Kind == Ddr4ProfileKind.Jedec)
            {
                SetMtb(9, (m, f) => { edit.RrdsTicks = m; edit.RrdsFc = f; });
                SetMtb(10, (m, f) => { edit.RrdlTicks = m; edit.RrdlFc = f; });
                SetMtb(11, (m, f) => { edit.CcdlTicks = m; edit.CcdlFc = f; });
                SetMtb(12, (m, _) => { edit.FawTicks = m; });
                SetMtb(13, (m, _) => { edit.WtrsTicks = m; });
                SetMtb(14, (m, _) => { edit.WtrlTicks = m; });
            }
            else
            {
                SetMtb(9, (m, f) => { edit.RrdsTicks = m; edit.RrdsFc = f; });
                SetMtb(10, (m, f) => { edit.RrdlTicks = m; edit.RrdlFc = f; });
                SetMtb(11, (m, _) => { edit.FawTicks = m; });
            }

            return edit;
        }

        private static Label Lbl(string text, int x, int y) => new()
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = UiFont,
        };

        private static Label Header(string text, int x, int y) => new()
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = HeaderFont,
        };

        private static void SetNud(NumericUpDown nud, int value)
        {
            decimal v = Math.Clamp(value, nud.Minimum, nud.Maximum);
            if (nud.Value != v) nud.Value = v;
        }

        private static NumericUpDown MakeNud(decimal value, decimal min, decimal max, bool editable, int width)
        {
            var nud = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Width = width,
                Height = 24,
                ThousandsSeparator = true,
                ReadOnly = !editable,
                Increment = editable ? 1 : 0,
                Font = SmallFont,
                TextAlign = HorizontalAlignment.Right,
            };
            try { nud.Value = Math.Clamp(value, min, max); }
            catch { nud.Value = min; }
            return nud;
        }
    }

    #endregion

    #region DDR5 binder

    private sealed class ProfilePageBinder
    {
        public Ddr5ProfileView Meta { get; }

        private ComboBox _cboSpeedBin = null!;
        private NumericUpDown _nudTck = null!;
        private Label _lblMhz = null!;
        private Label _lblMt = null!;
        private ComboBox? _cboCmdRate;
        private CheckBox? _chkBoost;
        private CheckBox? _chkRtOc;
        private TextBox? _txtName;
        private NumericUpDown? _nudVdd;
        private NumericUpDown? _nudVddq;
        private NumericUpDown? _nudVpp;
        private NumericUpDown? _nudVmem;
        private readonly Dictionary<int, CheckBox> _cas = [];
        private readonly NumericUpDown[] _leftPs = new NumericUpDown[9];
        private readonly NumericUpDown[] _leftTicks = new NumericUpDown[9];
        private readonly NumericUpDown[] _rightPs = new NumericUpDown[8];
        private readonly NumericUpDown[] _rightMin = new NumericUpDown[8];
        private readonly NumericUpDown[] _rightTicks = new NumericUpDown[8];
        private bool _syncing;

        private static readonly Font UiFont = new("Microsoft YaHei UI", 9F);
        private static readonly Font HeaderFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font SmallFont = new("Microsoft YaHei UI", 8.5F);
        private static readonly Font HintFont = new("Microsoft YaHei UI", 8.5F);

        public ProfilePageBinder(Ddr5ProfileView meta) => Meta = meta;

        public Panel BuildPanel()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4),
                Font = UiFont,
            };
            bool edit = Meta.Writable && Meta.Kind != Ddr5ProfileKind.Jedec;
            const int contentW = 980;
            int y = 4;

            if (Meta.Kind == Ddr5ProfileKind.Xmp)
            {
                var grpProfile = new GroupBox
                {
                    Text = "Profile 信息",
                    Location = new Point(4, y),
                    Size = new Size(contentW, 54),
                    Font = UiFont,
                };
                _txtName = new TextBox
                {
                    Location = new Point(100, 22),
                    Width = 220,
                    MaxLength = 16,
                    Text = Meta.ProfileName,
                    Enabled = edit,
                    Font = UiFont,
                };
                grpProfile.Controls.AddRange([Lbl("Profile 名称:", 16, 25), _txtName]);
                root.Controls.Add(grpProfile);
                y += 60;
            }

            int freqH = Meta.Kind == Ddr5ProfileKind.Xmp ? 130 : 78;
            var grpFreq = new GroupBox
            {
                Text = "频率 (Frequency) / Speed Bin",
                Location = new Point(4, y),
                Size = new Size(contentW, freqH),
                Font = UiFont,
            };
            BuildFrequency(grpFreq, edit);
            root.Controls.Add(grpFreq);
            y += freqH + 6;

            if (Meta.Kind is Ddr5ProfileKind.Xmp or Ddr5ProfileKind.Expo)
            {
                var grpVolt = new GroupBox
                {
                    Text = Meta.Kind == Ddr5ProfileKind.Xmp ? "电压 (Voltages) - DDR5" : "电压 (Voltages)",
                    Location = new Point(4, y),
                    Size = new Size(contentW, 54),
                    Font = UiFont,
                };
                BuildVoltage(grpVolt, edit);
                root.Controls.Add(grpVolt);
                y += 60;
            }

            if (Meta.Kind != Ddr5ProfileKind.Expo)
            {
                var grpCas = new GroupBox
                {
                    Text = "支持的 CAS Latency",
                    Location = new Point(4, y),
                    Size = new Size(contentW, 110),
                    Font = UiFont,
                };
                BuildCas(grpCas, edit);
                root.Controls.Add(grpCas);
                y += 116;
            }

            var grpTiming = new GroupBox
            {
                Text = "时序参数 (Timings) - DDR5",
                Location = new Point(4, y),
                Size = new Size(contentW, 360),
                Font = UiFont,
            };
            BuildTiming(grpTiming, edit);
            root.Controls.Add(grpTiming);

            ApplyToUi(FromMeta());
            return root;
        }

        private void BuildFrequency(GroupBox grp, bool edit)
        {
            grp.Controls.Add(Lbl("Speed Bin:", 16, 24));
            _cboSpeedBin = new ComboBox
            {
                Location = new Point(100, 20),
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = edit,
                Font = UiFont,
            };
            _cboSpeedBin.Items.Add("");
            foreach (var name in Ddr5SpeedBins.Names.OrderBy(n => n, StringComparer.Ordinal))
                _cboSpeedBin.Items.Add(name);
            _cboSpeedBin.SelectedIndex = 0;

            var btnApply = new Button
            {
                Text = "Apply",
                Location = new Point(270, 19),
                Size = new Size(64, 26),
                Enabled = edit,
                Font = UiFont,
            };
            btnApply.Click += (_, _) => ApplySpeedBin();

            var hint = new Label
            {
                Text = "选择 Speed Bin 后点击 Apply 自动填充参数",
                Location = new Point(344, 24),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = HintFont,
            };

            grp.Controls.Add(Lbl("Min Cycle Time (ps):", 16, 52));
            _nudTck = MakeNud(Meta.TckPs > 0 ? Meta.TckPs : 1, 1, 65535, edit, 90);
            _nudTck.Location = new Point(160, 48);
            _nudTck.ThousandsSeparator = false;
            _nudTck.ValueChanged += (_, _) => OnTckChanged();

            _lblMhz = new Label { Location = new Point(270, 52), Size = new Size(140, 22), Font = UiFont };
            _lblMt = new Label { Location = new Point(420, 52), Size = new Size(140, 22), Font = UiFont };

            grp.Controls.AddRange([_cboSpeedBin, btnApply, hint, _nudTck, _lblMhz, _lblMt]);

            if (Meta.Kind == Ddr5ProfileKind.Xmp)
            {
                grp.Controls.Add(Lbl("命令速率:", 16, 80));
                _cboCmdRate = new ComboBox
                {
                    Location = new Point(100, 76),
                    Width = 100,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Enabled = edit,
                    Font = UiFont,
                };
                _cboCmdRate.Items.AddRange(["Undefined", "1N", "2N", "3N"]);
                int cr = Math.Clamp(Meta.CommandRate, 0, 3);
                _cboCmdRate.SelectedIndex = cr;

                _chkBoost = new CheckBox
                {
                    Text = "Intel Dynamic Memory Boost",
                    Location = new Point(16, 104),
                    AutoSize = true,
                    Checked = Meta.IntelBoost,
                    Enabled = edit,
                    Font = UiFont,
                };
                _chkRtOc = new CheckBox
                {
                    Text = "Realtime Memory Frequency OC",
                    Location = new Point(280, 104),
                    AutoSize = true,
                    Checked = Meta.RealTimeOc,
                    Enabled = edit,
                    Font = UiFont,
                };
                grp.Controls.AddRange([_cboCmdRate, _chkBoost, _chkRtOc]);
            }
        }

        private void BuildVoltage(GroupBox grp, bool edit)
        {
            int vdd = CvOrDefault(Meta.VddRaw, 110);
            int vddq = CvOrDefault(Meta.VddqRaw, 110);
            int vpp = CvOrDefault(Meta.VppRaw, 180);
            int vmem = CvOrDefault(Meta.VmemRaw, 110);

            _nudVdd = MakeNud(vdd, 110, 300, edit, 64);
            _nudVddq = MakeNud(vddq, 110, 300, edit, 64);
            _nudVpp = MakeNud(vpp, 110, 300, edit, 64);
            _nudVdd.Location = new Point(52, 22);
            _nudVddq.Location = new Point(200, 22);
            _nudVpp.Location = new Point(348, 22);
            _nudVdd.ThousandsSeparator = false;
            _nudVddq.ThousandsSeparator = false;
            _nudVpp.ThousandsSeparator = false;

            grp.Controls.AddRange([
                Lbl("VDD:", 16, 25), _nudVdd,
                Lbl("VDDQ:", 140, 25), _nudVddq,
                Lbl("VPP:", 286, 25), _nudVpp,
            ]);

            if (Meta.Kind == Ddr5ProfileKind.Xmp)
            {
                _nudVmem = MakeNud(vmem, 110, 300, edit, 64);
                _nudVmem.Location = new Point(530, 22);
                _nudVmem.ThousandsSeparator = false;
                grp.Controls.AddRange([Lbl("VMEMCTRL:", 430, 25), _nudVmem]);
            }
        }

        private static int CvOrDefault(int raw, int fallback)
        {
            if (raw == 0) return fallback;
            int cv = SpdUtils.VoltageByteToCv(raw);
            return cv is >= 110 and <= 300 ? cv : fallback;
        }

        private void BuildCas(GroupBox grp, bool edit)
        {
            const int cols = 12;
            const int cellW = 76;
            const int cellH = 20;
            const int originX = 14;
            const int originY = 22;

            int idx = 0;
            for (int cl = 20; cl <= 98; cl += 2)
            {
                int row = idx / cols;
                int col = idx % cols;
                idx++;
                var chk = new CheckBox
                {
                    Text = cl.ToString(),
                    AutoSize = true,
                    Location = new Point(originX + col * cellW, originY + row * cellH),
                    Enabled = edit,
                    Checked = Meta.SupportedCas.Contains(cl),
                    Font = SmallFont,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                };
                _cas[cl] = chk;
                grp.Controls.Add(chk);
            }
        }

        private void BuildTiming(GroupBox grp, bool edit)
        {
            const int row0 = 44;
            const int rowH = 30;
            const int lName = 12, lPs = 110, lTick = 220;
            const int rName = 320, rPs = 430, rMin = 540, rTick = 650;

            grp.Controls.Add(Header("参数名", lName, 18));
            grp.Controls.Add(Header("值 (ps)", lPs, 18));
            grp.Controls.Add(Header("Ticks", lTick, 18));
            grp.Controls.Add(Header("参数名", rName, 18));
            grp.Controls.Add(Header("值 (ps)", rPs, 18));
            grp.Controls.Add(Header("Lower Limit", rMin, 18));
            grp.Controls.Add(Header("Ticks", rTick, 18));

            string[] leftNames =
            [
                "tAA", "tRCD", "tRP", "tRAS", "tRC", "tWR",
                "tRFC1_slr", "tRFC2_slr", "tRFCsb_slr",
            ];
            int[] leftVals =
            [
                Meta.TclPs, Meta.TrcdPs, Meta.TrpPs, Meta.TrasPs, Meta.TrcPs, Meta.TwrPs,
                Meta.Trfc1Ns, Meta.Trfc2Ns, Meta.TrfcsbNs,
            ];

            for (int i = 0; i < 9; i++)
            {
                int yy = row0 + i * rowH;
                bool isRfc = i >= 6;
                grp.Controls.Add(Lbl(leftNames[i] + ":", lName, yy + 4));
                _leftPs[i] = MakeNud(leftVals[i], 0, isRfc ? 65535 : 1_000_000, edit, 96);
                _leftPs[i].Location = new Point(lPs, yy);
                _leftTicks[i] = MakeNud(0, 0, 65535, edit, 64);
                _leftTicks[i].Location = new Point(lTick, yy);
                int li = i;
                _leftPs[i].ValueChanged += (_, _) => OnPsChanged(li, isLeft: true);
                _leftTicks[i].ValueChanged += (_, _) => OnTickChanged(li, isLeft: true);
                grp.Controls.AddRange([_leftPs[i], _leftTicks[i]]);
            }

            (string Name, int Val, int Min)[] right =
            [
                ("tRRD_L", Meta.TrrdlPs, Meta.TrrdlMin),
                ("tCCD_L", Meta.TccdLPs, Meta.TccdLMin),
                ("tCCD_L_WR", Meta.TccdLWrPs, Meta.TccdLWrMin),
                ("tCCD_L_WR2", Meta.TccdLWr2Ps, Meta.TccdLWr2Min),
                ("tFAW", Meta.TfawPs, Meta.TfawMin),
                ("tCCD_L_WTR", Meta.TccdLWtrPs, Meta.TccdLWtrMin),
                ("tCCD_S_WTR", Meta.TccdSWtrPs, Meta.TccdSWtrMin),
                ("tRTP", Meta.TrtpPs, Meta.TrtpMin),
            ];
            bool minEdit = edit && Meta.Kind != Ddr5ProfileKind.Expo;
            for (int i = 0; i < 8; i++)
            {
                int yy = row0 + i * rowH;
                grp.Controls.Add(Lbl(right[i].Name + ":", rName, yy + 4));
                _rightPs[i] = MakeNud(right[i].Val, 0, 1_000_000, edit, 96);
                _rightPs[i].Location = new Point(rPs, yy);
                _rightMin[i] = MakeNud(right[i].Min, 0, 255, minEdit, 64);
                _rightMin[i].Location = new Point(rMin, yy);
                _rightTicks[i] = MakeNud(0, 0, 65535, edit, 64);
                _rightTicks[i].Location = new Point(rTick, yy);
                int ri = i;
                _rightPs[i].ValueChanged += (_, _) => OnPsChanged(ri, isLeft: false);
                _rightTicks[i].ValueChanged += (_, _) => OnTickChanged(ri, isLeft: false);
                _rightMin[i].ValueChanged += (_, _) =>
                {
                    if (!_syncing) RefreshTicksFromPs();
                };
                grp.Controls.AddRange([_rightPs[i], _rightMin[i], _rightTicks[i]]);
            }
        }

        private void ApplySpeedBin()
        {
            string? name = _cboSpeedBin.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请先选择 Speed Bin。", "Speed Bin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Ddr5SpeedBins.TryApply(name, Meta.Kind, out var edit, out string err))
            {
                MessageBox.Show(err, "Speed Bin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_txtName != null)
                edit.ProfileName = _txtName.Text.Trim();

            ApplyToUi(edit);
        }

        private Ddr5ProfileEdit FromMeta() => new()
        {
            TckPs = Meta.TckPs,
            SupportedCas = new HashSet<int>(Meta.SupportedCas),
            TclPs = Meta.TclPs,
            TrcdPs = Meta.TrcdPs,
            TrpPs = Meta.TrpPs,
            TrasPs = Meta.TrasPs,
            TrcPs = Meta.TrcPs,
            TwrPs = Meta.TwrPs,
            Trfc1Ns = Meta.Trfc1Ns,
            Trfc2Ns = Meta.Trfc2Ns,
            TrfcsbNs = Meta.TrfcsbNs,
            TrrdlPs = Meta.TrrdlPs,
            TrrdlMin = Meta.TrrdlMin,
            TccdLPs = Meta.TccdLPs,
            TccdLMin = Meta.TccdLMin,
            TccdLWrPs = Meta.TccdLWrPs,
            TccdLWrMin = Meta.TccdLWrMin,
            TccdLWr2Ps = Meta.TccdLWr2Ps,
            TccdLWr2Min = Meta.TccdLWr2Min,
            TfawPs = Meta.TfawPs,
            TfawMin = Meta.TfawMin,
            TccdLWtrPs = Meta.TccdLWtrPs,
            TccdLWtrMin = Meta.TccdLWtrMin,
            TccdSWtrPs = Meta.TccdSWtrPs,
            TccdSWtrMin = Meta.TccdSWtrMin,
            TrtpPs = Meta.TrtpPs,
            TrtpMin = Meta.TrtpMin,
            VddCv = CvOrDefault(Meta.VddRaw, 110),
            VddqCv = CvOrDefault(Meta.VddqRaw, 110),
            VppCv = CvOrDefault(Meta.VppRaw, 180),
            VmemCv = CvOrDefault(Meta.VmemRaw, 110),
            CommandRate = Meta.CommandRate,
            IntelBoost = Meta.IntelBoost,
            RealTimeOc = Meta.RealTimeOc,
            ProfileName = Meta.ProfileName,
        };

        private void ApplyToUi(Ddr5ProfileEdit e)
        {
            _syncing = true;
            try
            {
                SetNud(_nudTck, e.TckPs > 0 ? e.TckPs : 1);
                UpdateFreqLabels((int)_nudTck.Value);

                if (_cboCmdRate != null)
                    _cboCmdRate.SelectedIndex = Math.Clamp(e.CommandRate, 0, 3);
                if (_chkBoost != null) _chkBoost.Checked = e.IntelBoost;
                if (_chkRtOc != null) _chkRtOc.Checked = e.RealTimeOc;
                if (_txtName != null && !string.IsNullOrEmpty(e.ProfileName))
                    _txtName.Text = e.ProfileName;

                if (_nudVdd != null) SetNud(_nudVdd, e.VddCv);
                if (_nudVddq != null) SetNud(_nudVddq, e.VddqCv);
                if (_nudVpp != null) SetNud(_nudVpp, e.VppCv);
                if (_nudVmem != null) SetNud(_nudVmem, e.VmemCv);

                foreach (var (cl, chk) in _cas)
                    chk.Checked = e.SupportedCas.Contains(cl);

                SetNud(_leftPs[0], e.TclPs);
                SetNud(_leftPs[1], e.TrcdPs);
                SetNud(_leftPs[2], e.TrpPs);
                SetNud(_leftPs[3], e.TrasPs);
                SetNud(_leftPs[4], e.TrcPs);
                SetNud(_leftPs[5], e.TwrPs);
                SetNud(_leftPs[6], e.Trfc1Ns);
                SetNud(_leftPs[7], e.Trfc2Ns);
                SetNud(_leftPs[8], e.TrfcsbNs);

                SetNud(_rightPs[0], e.TrrdlPs); SetNud(_rightMin[0], e.TrrdlMin);
                SetNud(_rightPs[1], e.TccdLPs); SetNud(_rightMin[1], e.TccdLMin);
                SetNud(_rightPs[2], e.TccdLWrPs); SetNud(_rightMin[2], e.TccdLWrMin);
                SetNud(_rightPs[3], e.TccdLWr2Ps); SetNud(_rightMin[3], e.TccdLWr2Min);
                SetNud(_rightPs[4], e.TfawPs); SetNud(_rightMin[4], e.TfawMin);
                SetNud(_rightPs[5], e.TccdLWtrPs); SetNud(_rightMin[5], e.TccdLWtrMin);
                SetNud(_rightPs[6], e.TccdSWtrPs); SetNud(_rightMin[6], e.TccdSWtrMin);
                SetNud(_rightPs[7], e.TrtpPs); SetNud(_rightMin[7], e.TrtpMin);

                RefreshTicksFromPs();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnTckChanged()
        {
            if (_syncing) return;
            UpdateFreqLabels((int)_nudTck.Value);
            RefreshTicksFromPs();
        }

        private void UpdateFreqLabels(int tckPs)
        {
            int mhz = tckPs > 0 ? (int)Math.Round(1_000_000.0 / tckPs) : 0;
            int mt = tckPs > 0 ? (int)Math.Round(SpdUtils.MtFromMinCyclePs(tckPs)) : 0;
            _lblMhz.Text = $"{mhz} MHz";
            _lblMt.Text = $"{mt} MT/s";
        }

        private int CurrentTckPs() => (int)_nudTck.Value;

        private void RefreshTicksFromPs()
        {
            bool was = _syncing;
            _syncing = true;
            try
            {
                int tck = CurrentTckPs();
                for (int i = 0; i < 6; i++)
                    SetNud(_leftTicks[i], Nck((int)_leftPs[i].Value, tck));
                for (int i = 6; i < 9; i++)
                    SetNud(_leftTicks[i], Nck((int)_leftPs[i].Value * 1000, tck));

                for (int i = 0; i < 8; i++)
                {
                    int n = Nck((int)_rightPs[i].Value, tck);
                    int min = (int)_rightMin[i].Value;
                    SetNud(_rightTicks[i], Math.Max(n, min));
                }
            }
            finally
            {
                _syncing = was;
            }
        }

        private void OnPsChanged(int index, bool isLeft)
        {
            if (_syncing) return;
            int tck = CurrentTckPs();
            _syncing = true;
            try
            {
                if (isLeft)
                {
                    int ps = (int)_leftPs[index].Value;
                    int ticks = index >= 6 ? Nck(ps * 1000, tck) : Nck(ps, tck);
                    SetNud(_leftTicks[index], ticks);
                }
                else
                {
                    int n = Nck((int)_rightPs[index].Value, tck);
                    SetNud(_rightTicks[index], Math.Max(n, (int)_rightMin[index].Value));
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnTickChanged(int index, bool isLeft)
        {
            if (_syncing) return;
            int tck = CurrentTckPs();
            if (tck <= 0) return;
            _syncing = true;
            try
            {
                int ticks = isLeft ? (int)_leftTicks[index].Value : (int)_rightTicks[index].Value;
                int ps = SpdUtils.TicksToTimeDdr5(ticks, tck);
                if (isLeft)
                {
                    if (index >= 6)
                        SetNud(_leftPs[index], (int)Math.Round(ps / 1000.0));
                    else
                        SetNud(_leftPs[index], ps);
                }
                else
                {
                    SetNud(_rightPs[index], ps);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private static int Nck(int timePs, int tck) =>
            tck > 0 && timePs > 0 ? SpdUtils.TimeToTicksDdr5(timePs, tck) : 0;

        public Ddr5ProfileEdit Collect()
        {
            var cas = new HashSet<int>();
            foreach (var (cl, chk) in _cas)
            {
                if (chk.Checked) cas.Add(cl);
            }

            return new Ddr5ProfileEdit
            {
                TckPs = CurrentTckPs(),
                SupportedCas = cas,
                TclPs = (int)_leftPs[0].Value,
                TrcdPs = (int)_leftPs[1].Value,
                TrpPs = (int)_leftPs[2].Value,
                TrasPs = (int)_leftPs[3].Value,
                TrcPs = (int)_leftPs[4].Value,
                TwrPs = (int)_leftPs[5].Value,
                Trfc1Ns = (int)_leftPs[6].Value,
                Trfc2Ns = (int)_leftPs[7].Value,
                TrfcsbNs = (int)_leftPs[8].Value,
                TrrdlPs = (int)_rightPs[0].Value,
                TrrdlMin = (int)_rightMin[0].Value,
                TccdLPs = (int)_rightPs[1].Value,
                TccdLMin = (int)_rightMin[1].Value,
                TccdLWrPs = (int)_rightPs[2].Value,
                TccdLWrMin = (int)_rightMin[2].Value,
                TccdLWr2Ps = (int)_rightPs[3].Value,
                TccdLWr2Min = (int)_rightMin[3].Value,
                TfawPs = (int)_rightPs[4].Value,
                TfawMin = (int)_rightMin[4].Value,
                TccdLWtrPs = (int)_rightPs[5].Value,
                TccdLWtrMin = (int)_rightMin[5].Value,
                TccdSWtrPs = (int)_rightPs[6].Value,
                TccdSWtrMin = (int)_rightMin[6].Value,
                TrtpPs = (int)_rightPs[7].Value,
                TrtpMin = (int)_rightMin[7].Value,
                VddCv = (int)(_nudVdd?.Value ?? CvOrDefault(Meta.VddRaw, 110)),
                VddqCv = (int)(_nudVddq?.Value ?? CvOrDefault(Meta.VddqRaw, 110)),
                VppCv = (int)(_nudVpp?.Value ?? CvOrDefault(Meta.VppRaw, 180)),
                VmemCv = (int)(_nudVmem?.Value ?? CvOrDefault(Meta.VmemRaw, 110)),
                CommandRate = _cboCmdRate?.SelectedIndex ?? Meta.CommandRate,
                IntelBoost = _chkBoost?.Checked ?? Meta.IntelBoost,
                RealTimeOc = _chkRtOc?.Checked ?? Meta.RealTimeOc,
                ProfileName = _txtName?.Text.Trim() ?? Meta.ProfileName,
            };
        }

        private static Label Lbl(string text, int x, int y) => new()
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = UiFont,
        };

        private static Label Header(string text, int x, int y) => new()
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = HeaderFont,
        };

        private static void SetNud(NumericUpDown nud, int value)
        {
            decimal v = Math.Clamp(value, nud.Minimum, nud.Maximum);
            if (nud.Value != v) nud.Value = v;
        }

        private static NumericUpDown MakeNud(decimal value, decimal min, decimal max, bool editable, int width)
        {
            var nud = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Width = width,
                Height = 24,
                ThousandsSeparator = true,
                ReadOnly = !editable,
                Increment = editable ? 1 : 0,
                Font = SmallFont,
                TextAlign = HorizontalAlignment.Right,
            };
            try { nud.Value = Math.Clamp(value, min, max); }
            catch { nud.Value = min; }
            return nud;
        }
    }

    #endregion
}

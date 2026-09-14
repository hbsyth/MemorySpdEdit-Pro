namespace SpdEditor;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        Text = "DDR4/DDR5 内存 SPD 信息安全编辑器 V3.0";
        Size = new Size(920, 680);
        MinimumSize = new Size(860, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(240, 240, 240);

        // ===== Top toolbar =====
        _toolbarPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 4) };

        _lblOnline = new Label
        {
            Text = "离线",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Location = new Point(8, 14),
        };

        _cmbPort = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
            Location = new Point(60, 10),
        };
        _cmbPort.SelectedIndexChanged += (_, _) => TryConnectPort();

        _btnRead = MakeToolButton("读取BIN", Color.FromArgb(46, 139, 87));
        _btnLoad = MakeToolButton("载入BIN", Color.FromArgb(30, 144, 255));
        _btnBackup = MakeToolButton("保存BIN", Color.FromArgb(138, 43, 226));
        _btnUnlock = MakeToolButton("SPD解锁", Color.FromArgb(255, 140, 0));
        _btnLock = MakeToolButton("SPD上锁", Color.FromArgb(220, 20, 60));
        _btnXmp = MakeToolButton("高级参数修改", Color.FromArgb(70, 130, 180));

        _btnRead.Click += async (_, _) => await ReadFromChipAsync();
        _btnLoad.Click += (_, _) => LoadBinFile();
        _btnBackup.Click += (_, _) => BackupBin();
        _btnUnlock.Click += (_, _) => UnlockSpd();
        _btnLock.Click += (_, _) => LockSpd();
        _btnXmp.Click += (_, _) => OpenAdvancedEditor();

        _toolbarPanel.Controls.AddRange([_lblOnline, _cmbPort, _btnRead, _btnLoad, _btnBackup, _btnUnlock, _btnLock, _btnXmp]);

        // ===== Main content =====
        var mainSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
        };
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        // --- Left column ---
        var leftPanel = new Panel { Dock = DockStyle.Fill };

        const int sectionPadX = 12;
        const int sectionContentW = 336;
        const int sectionBtnGap = 15;

        var grpQuick = MakeGroupBox("一键快捷操作", new Point(0, 0), new Size(360, 72));
        var btnRandom = MakeActionButton("随机型号/SN", Color.FromArgb(60, 179, 113), Point.Empty);
        var btnRestore = MakeActionButton("还原初始状态", Color.FromArgb(60, 179, 113), Point.Empty);
        btnRandom.Click += (_, _) => QuickRandom();
        btnRestore.Click += (_, _) => RestoreInitial();

        const int quickBtnH = 32;
        const int quickBtnW = (sectionContentW - sectionBtnGap) / 2;
        const int quickBtnW2 = sectionContentW - sectionBtnGap - quickBtnW;
        var quickHost = new Panel { Dock = DockStyle.Fill };
        quickHost.Controls.AddRange([btnRandom, btnRestore]);
        void LayoutQuickButtons()
        {
            int y = Math.Max(0, (quickHost.ClientSize.Height - quickBtnH) / 2);
            btnRandom.SetBounds(sectionPadX, y, quickBtnW, quickBtnH);
            btnRestore.SetBounds(sectionPadX + quickBtnW + sectionBtnGap, y, quickBtnW2, quickBtnH);
        }
        quickHost.Resize += (_, _) => LayoutQuickButtons();
        grpQuick.Controls.Add(quickHost);
        LayoutQuickButtons();

        var grpParams = MakeGroupBox("参数详细修改", new Point(0, 80), new Size(360, 302));
        const int paramFieldX = 100;
        const int paramFieldW = 100;
        const int paramBtnW = 46;
        const int paramBtnGap = 6;
        const int paramBtnX = paramFieldX + paramFieldW + 8;
        const int paramActionW = paramFieldW + 8 + 3 * paramBtnW + 2 * paramBtnGap;
        Button MakeParamButton(string text, int rowY, int index) => new()
        {
            Text = text,
            Location = new Point(paramBtnX + index * (paramBtnW + paramBtnGap), rowY - 4),
            Size = new Size(paramBtnW, 26),
            FlatStyle = FlatStyle.Standard,
        };
        const int wideBtnGap = sectionBtnGap;
        const int wideBtnW = (paramActionW - wideBtnGap * 2) / 3;
        Button MakeWideParamButton(string text, int rowY, int index) => new()
        {
            Text = text,
            Location = new Point(paramFieldX + index * (wideBtnW + wideBtnGap), rowY - 4),
            Size = new Size(wideBtnW, 26),
            FlatStyle = FlatStyle.Standard,
        };
        int y = 28;
        grpParams.Controls.Add(MakeLabel("内存品牌:", 12, y));
        _cmbModuleBrand = new ComboBox { Location = new Point(100, y - 3), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var b in Core.JedecManufacturers.Brands) _cmbModuleBrand.Items.Add(b.Name);
        _cmbModuleBrand.SelectedIndex = 0;
        y += 36;
        grpParams.Controls.Add(MakeLabel("生产日期:", 12, y));
        _txtDate = new TextBox { Location = new Point(paramFieldX, y - 3), Width = paramFieldW, MaxLength = 4, Text = "0000" };
        var btnDateRand = MakeParamButton("随机", y, 0);
        var btnDateClear = MakeParamButton("清零", y, 1);
        var btnDateRead = MakeParamButton("读取", y, 2);
        btnDateRand.Click += (_, _) => _txtDate.Text = Core.SpdEditorLogic.RandomProductionDate();
        btnDateClear.Click += (_, _) => _txtDate.Text = "0000";
        btnDateRead.Click += (_, _) => ReadFieldFromSpd(i => _txtDate.Text = i.ProductionDate);
        grpParams.Controls.AddRange([_txtDate, btnDateRand, btnDateClear, btnDateRead]);
        y += 36;
        grpParams.Controls.Add(MakeLabel("序列号SN:", 12, y));
        _txtSn = new TextBox { Location = new Point(paramFieldX, y - 3), Width = paramFieldW, Text = "00000000" };
        var btnSnRand = MakeParamButton("随机", y, 0);
        var btnSnClear = MakeParamButton("清零", y, 1);
        var btnSnRead = MakeParamButton("读取", y, 2);
        btnSnRand.Click += (_, _) => _txtSn.Text = Core.SpdEditorLogic.RandomSerial();
        btnSnClear.Click += (_, _) => _txtSn.Text = "00000000";
        btnSnRead.Click += (_, _) => ReadFieldFromSpd(i => _txtSn.Text = i.SerialNumber);
        grpParams.Controls.AddRange([_txtSn, btnSnRand, btnSnClear, btnSnRead]);
        y += 36;
        grpParams.Controls.Add(MakeLabel("产品型号:", 12, y));
        _txtModel = new TextBox { Location = new Point(paramFieldX, y - 3), Width = paramActionW };
        int modelBtnY = y + 30;
        var btnModelRand = MakeWideParamButton("随机", modelBtnY, 0);
        var btnModelClear = MakeWideParamButton("清零", modelBtnY, 1);
        var btnModelRead = MakeWideParamButton("读取", modelBtnY, 2);
        btnModelRand.Click += (_, _) => _txtModel.Text = RandomModelForSelectedBrand();
        btnModelClear.Click += (_, _) => _txtModel.Text = "";
        btnModelRead.Click += (_, _) => ReadFieldFromSpd(i => _txtModel.Text = i.PartNumber);
        grpParams.Controls.AddRange([_txtModel, btnModelRand, btnModelClear, btnModelRead]);
        y += 58;
        grpParams.Controls.Add(MakeLabel("颗粒制造商:", 12, y));
        _cmbDieBrand = new ComboBox { Location = new Point(100, y - 3), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var b in Core.JedecManufacturers.Brands) _cmbDieBrand.Items.Add(b.Name);
        _cmbDieBrand.SelectedIndex = 1;
        grpParams.Controls.Add(_cmbDieBrand);
        y += 40;
        var btnSuggest = MakeSmallButton("修改建议", new Point(sectionPadX, y));
        btnSuggest.Size = new Size(sectionContentW, 28);
        btnSuggest.Click += (_, _) => MessageBox.Show(
            "修改建议:\n• 批量生产时启用批量模式，SN 自动递增\n• 写入前请先备份原始 Bin\n• 修改品牌/颗粒需与 XMP 配置匹配",
            "修改建议", MessageBoxButtons.OK, MessageBoxIcon.Information);
        grpParams.Controls.Add(btnSuggest);
        grpParams.Controls.Add(_cmbModuleBrand);

        var grpBatch = MakeGroupBox("批量修改选项", new Point(0, 390), new Size(360, 80));
        _chkBatch = new CheckBox
        {
            Text = "启用批量模式 (写入成功后自动递增)",
            Location = new Point(12, 28),
            AutoSize = true,
        };
        var lblStep = MakeLabel("递增步长:", 12, 52);
        _numStep = new NumericUpDown
        {
            Location = new Point(90, 48),
            Width = 60,
            Minimum = 1,
            Maximum = 9999,
            Value = 1,
        };
        grpBatch.Controls.AddRange([_chkBatch, lblStep, _numStep]);

        leftPanel.Controls.AddRange([grpQuick, grpParams, grpBatch]);

        // --- Right column ---
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };

        var grpInfo = MakeGroupBox("原始数据读取信息", new Point(0, 0), new Size(500, 110));
        grpInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtInfo = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Location = new Point(12, 24),
            Size = new Size(470, 78),
            Font = new Font("Consolas", 9F),
            Text = "尚未读取数据",
        };
        grpInfo.Controls.Add(_txtInfo);

        var grpLog = MakeGroupBox("系统实时日志", new Point(0, 118), new Size(500, 330));
        grpLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _txtLog = new RichTextBox
        {
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = Color.LimeGreen,
            Font = new Font("Consolas", 9F),
            Location = new Point(12, 24),
            Size = new Size(470, 296),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        grpLog.Controls.Add(_txtLog);

        rightPanel.Controls.AddRange([grpInfo, grpLog]);

        mainSplit.Controls.Add(leftPanel, 0, 0);
        mainSplit.Controls.Add(rightPanel, 1, 0);

        // ===== Bottom write button =====
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8) };
        _btnWrite = new Button
        {
            Text = "烧录内存",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 20, 60),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _btnWrite.FlatAppearance.BorderSize = 0;
        _btnWrite.Click += async (_, _) => await WriteToChipAsync();
        bottomPanel.Controls.Add(_btnWrite);

        Controls.Add(mainSplit);
        Controls.Add(bottomPanel);
        Controls.Add(_toolbarPanel);
    }

    private static Button MakeToolButton(string text, Color bg)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Padding = new Padding(2, 0, 2, 0),
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static Button MakeActionButton(string text, Color bg, Point loc)
    {
        var btn = new Button
        {
            Text = text,
            Location = loc,
            Size = new Size(110, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static Button MakeSmallButton(string text, Point loc)
    {
        return new Button
        {
            Text = text,
            Location = loc,
            Size = new Size(70, 26),
            FlatStyle = FlatStyle.Standard,
        };
    }

    private static Label MakeLabel(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static GroupBox MakeGroupBox(string title, Point loc, Size size) =>
        new()
        {
            Text = title,
            Location = loc,
            Size = size,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
}

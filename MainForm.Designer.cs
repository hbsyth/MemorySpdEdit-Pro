#nullable enable
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
        Text = "DDR3/DDR4/DDR5 内存SPD信息修改器 by SuperGun  Ver.001";
        Size = new Size(1100, 720);
        // 覆盖固定工具栏 6×150 + 间距，缩放时工具栏尺寸不变
        MinimumSize = new Size(1080, 700);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* ignore */ }
        Font = new Font("Microsoft YaHei UI", 9F);
        // 跟随系统主题（浅色/深色），勿写死灰色背景
        BackColor = SystemColors.Control;

        // ===== Top toolbar (3 rows) =====
        _toolbarPanel = new Panel { Dock = DockStyle.Top, Height = 130, Padding = new Padding(12, 10, 12, 10) };

        _lblOnline = new Label
        {
            Text = "离线",
            AutoSize = false,
            Width = 100,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        };

        _lnkDonate = new LinkLabel
        {
            Text = "好用就打赏一下作者吧",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _lnkDonate.LinkClicked += (_, _) => ShowAboutDialog();

        _lblPort = new Label
        {
            Text = "端口：",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _cmbPort = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
        };

        _cmbDeviceType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
        };
        _cmbDeviceType.Items.AddRange([
            new DeviceTypeItem(4, "DDR4 (D4)"),
            new DeviceTypeItem(5, "DDR5 (D5)"),
            new DeviceTypeItem(3, "DDR3 (D3)"),
        ]);
        _cmbDeviceType.SelectedIndex = 0;
        _cmbDeviceType.SelectedIndexChanged += (_, _) => SyncDeviceTypeFromUi();

        _btnOpenPort = MakeToolButton("打开端口", Color.FromArgb(46, 139, 87));
        _btnClosePort = MakeToolButton("关闭端口", Color.FromArgb(105, 105, 105));
        _btnRefreshPort = MakeToolButton("刷新端口", Color.FromArgb(70, 130, 180));
        _btnRead = MakeToolButton("读取BIN", Color.FromArgb(46, 139, 87));
        _btnLoad = MakeToolButton("载入BIN", Color.FromArgb(30, 144, 255));
        _btnBackup = MakeToolButton("保存BIN", Color.FromArgb(138, 43, 226));
        _btnUnlock = MakeToolButton("SPD解锁", Color.FromArgb(255, 140, 0));
        _btnLock = MakeToolButton("SPD上锁", Color.FromArgb(220, 20, 60));

        _btnOpenPort.Click += (_, _) => OpenPort();
        _btnClosePort.Click += (_, _) => ClosePort();
        _btnRefreshPort.Click += (_, _) => RefreshPorts(selectFirstIfNeeded: false, logResult: true);
        _cmbPort.SelectedIndexChanged += (_, _) =>
        {
            SyncChannelFromPortSelection();
            UpdateOnlineStatus();
        };
        _btnRead.Click += async (_, _) => await ReadFromChipAsync();
        _btnLoad.Click += (_, _) => LoadBinFile();
        _btnBackup.Click += (_, _) => BackupBin();
        _btnUnlock.Click += (_, _) => UnlockSpd();
        _btnLock.Click += (_, _) => LockSpd();

        _toolbarPanel.Controls.AddRange([
            _lblOnline, _lnkDonate, _lblPort, _cmbPort, _cmbDeviceType, _btnOpenPort, _btnClosePort, _btnRefreshPort,
            _btnRead, _btnLoad, _btnBackup, _btnUnlock, _btnLock,
        ]);

        // ===== Main content =====
        var mainSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
        };
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- Left column ---
        var leftPanel = new Panel { Dock = DockStyle.Fill };

        const int sectionPadX = 12;
        const int sectionContentW = 336;
        const int sectionBtnGap = 15;

        var grpQuick = MakeFixedGroupBox("一键快捷操作", new Point(0, 0), new Size(360, 72));
        var btnRandom = MakeActionButton("一键随机", Color.FromArgb(60, 179, 113), Point.Empty);
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

        var grpParams = MakeFixedGroupBox("参数详细修改", new Point(0, 80), new Size(360, 270));
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
        _cmbModuleBrand = new ComboBox
        {
            Location = new Point(paramFieldX, y - 3),
            Width = paramActionW,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var b in Core.JedecManufacturers.ModuleBrands)
            _cmbModuleBrand.Items.Add(b.DisplayLabel);
        _cmbModuleBrand.SelectedIndex = Core.JedecManufacturers.UnknownModuleBrandIndex;
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
        grpParams.Controls.Add(MakeLabel("颗粒厂家:", 12, y));
        _cmbDieBrand = new ComboBox
        {
            Location = new Point(paramFieldX, y - 3),
            Width = paramActionW,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var d in Core.JedecManufacturers.DieManufacturers)
            _cmbDieBrand.Items.Add(d.DisplayLabel);
        _cmbDieBrand.SelectedIndex = Core.JedecManufacturers.UnknownDieBrandIndex;
        grpParams.Controls.Add(_cmbDieBrand);
        grpParams.Controls.Add(_cmbModuleBrand);

        var grpBatch = MakeFixedGroupBox("批量修改选项", new Point(0, 358), new Size(360, 108));
        _chkBatch = new CheckBox
        {
            Text = "启用批量模式 (写入成功后自动递增)",
            Location = new Point(12, 24),
            AutoSize = true,
        };
        var lblStep = MakeLabel("递增步长:", 12, 50);
        _numStep = new NumericUpDown
        {
            Location = new Point(90, 46),
            Width = 60,
            Minimum = 1,
            Maximum = 9999,
            Value = 1,
        };
        _chkBackupOnBurn = new CheckBox
        {
            Text = "烧录时备份原始BIN",
            Location = new Point(12, 76),
            AutoSize = true,
            Checked = true,
        };
        grpBatch.Controls.AddRange([_chkBatch, lblStep, _numStep, _chkBackupOnBurn]);

        leftPanel.Controls.AddRange([grpQuick, grpParams, grpBatch]);

        // --- Right column ---
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4, 0, 0, 0),
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var grpInfo = new GroupBox
        {
            Text = "原始数据读取信息",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
        };
        var infoHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
        _txtInfo = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.WindowText,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            Text = "尚未读取数据",
        };
        infoHost.Controls.Add(_txtInfo);
        grpInfo.Controls.Add(infoHost);

        var grpLog = new GroupBox
        {
            Text = "系统实时日志",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
        _txtLog = new RichTextBox
        {
            ReadOnly = true,
            BackColor = Color.Black,
            ForeColor = Color.LimeGreen,
            Font = new Font("Consolas", 9F),
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        logHost.Controls.Add(_txtLog);
        grpLog.Controls.Add(logHost);

        rightPanel.Controls.Add(grpInfo, 0, 0);
        rightPanel.Controls.Add(grpLog, 0, 1);

        mainSplit.Controls.Add(leftPanel, 0, 0);
        mainSplit.Controls.Add(rightPanel, 1, 0);

        // ===== Bottom write button =====
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8) };
        _btnWrite = new Button
        {
            Text = "烧录内存SPD信息",
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

    private static void ShowAboutDialog()
    {
        const int pad = 16;
        const int qrW = 200;
        const int qrH = 300;
        const int qrGap = 16;
        const int btnH = 30;
        int clientW = pad + qrW + qrGap + qrW + pad;
        int infoH = 120;
        int clientH = pad + infoH + 8 + qrH + 12 + btnH + pad;

        using var dlg = new Form
        {
            Text = "关于",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(clientW, clientH),
            Font = new Font("Microsoft YaHei UI", 9F),
            ShowInTaskbar = false,
        };

        var lblInfo = new Label
        {
            AutoSize = false,
            Location = new Point(pad, pad),
            Size = new Size(clientW - pad * 2, infoH),
            Text =
                "我的邮箱：hbsyth@qq.com\n" +
                "QQ：2247718170\n" +
                "支付宝打赏：th1qth@163.com\n\n" +
                "DDR3/DDR4/DDR5 内存SPD信息修改器 by SuperGun  Ver.001",
        };

        int qrY = pad + infoH + 8;
        var picAlipay = new PictureBox
        {
            Location = new Point(pad, qrY),
            Size = new Size(qrW, qrH),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
        };
        var picWechat = new PictureBox
        {
            Location = new Point(pad + qrW + qrGap, qrY),
            Size = new Size(qrW, qrH),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
        };

        Image? alipayImage = LoadEmbeddedImage("SpdEditor.Assets.alipay-qr.png");
        Image? wechatImage = LoadEmbeddedImage("SpdEditor.Assets.wechat-qr.png");
        picAlipay.Image = alipayImage;
        picWechat.Image = wechatImage;

        var btnOk = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Size = new Size(88, btnH),
            Location = new Point(clientW - pad - 88, clientH - pad - btnH),
        };
        dlg.AcceptButton = btnOk;
        dlg.Controls.AddRange([lblInfo, picAlipay, picWechat, btnOk]);
        dlg.ShowDialog();
        alipayImage?.Dispose();
        wechatImage?.Dispose();
    }

    private static Image? LoadEmbeddedImage(string resourceName)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var temp = Image.FromStream(stream);
            return new Bitmap(temp);
        }
        catch
        {
            return null;
        }
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

    private static Label MakeLabel(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static GroupBox MakeFixedGroupBox(string title, Point loc, Size size) =>
        new()
        {
            Text = title,
            Location = loc,
            Size = size,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
}

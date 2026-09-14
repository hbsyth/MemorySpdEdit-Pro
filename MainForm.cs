using SpdEditor.Core;

namespace SpdEditor;

public partial class MainForm : Form
{
    private readonly SerialDeviceService _serial = new();
    private byte[] _spdData = [];
    private byte[] _originalData = [];
    private byte[] _savedData = [];
    private SpdInfo _spdInfo = new();
    private int _deviceType = 5;
    private int _i2cAddress = 0x50;
    private bool _isReading;
    // Toolbar
    private Panel _toolbarPanel = null!;
    private Label _lblOnline = null!;
    private ComboBox _cmbPort = null!;
    private Button _btnRead = null!;
    private Button _btnLoad = null!;
    private Button _btnBackup = null!;
    private Button _btnUnlock = null!;
    private Button _btnLock = null!;
    private Button _btnXmp = null!;

    // Left panel
    private ComboBox _cmbModuleBrand = null!;
    private TextBox _txtDate = null!;
    private TextBox _txtSn = null!;
    private TextBox _txtModel = null!;
    private ComboBox _cmbDieBrand = null!;
    private CheckBox _chkBatch = null!;
    private NumericUpDown _numStep = null!;

    // Right panel
    private TextBox _txtInfo = null!;
    private RichTextBox _txtLog = null!;
    private Button _btnWrite = null!;

    public MainForm()
    {
        InitializeComponent();
        WireEvents();
        RefreshPorts();
        LayoutToolbarButtons();
        Log("系统就绪，请选择 COM 端口并连接设备。");
    }

    private void WireEvents()
    {
        _serial.LineReceived += OnSerialLine;
        _serial.ErrorOccurred += msg => RunOnUi(() => Log($"[ERR] {msg}", true));

        Load += (_, _) =>
        {
            RefreshPorts();
            LayoutToolbarButtons();
        };
        _toolbarPanel.Resize += (_, _) => LayoutToolbarButtons();
    }

    private void LayoutToolbarButtons()
    {
        const int btnHeight = 28;
        const int gap = 12;
        const int leftGap = 10;

        var buttons = new[] { _btnRead, _btnLoad, _btnBackup, _btnUnlock, _btnLock, _btnXmp };
        int btnWidth = 0;
        foreach (var btn in buttons)
        {
            int w = TextRenderer.MeasureText(btn.Text, btn.Font).Width + 16;
            btnWidth = Math.Max(btnWidth, w);
        }

        int startX = _cmbPort.Right + leftGap;
        int y = Math.Max(0, (_toolbarPanel.ClientSize.Height - btnHeight) / 2);
        int x = startX;
        foreach (var btn in buttons)
        {
            btn.SetBounds(x, y, btnWidth, btnHeight);
            x += btnWidth + gap;
        }
    }

    private void RefreshPorts()
    {
        var ports = SerialDeviceService.GetPortNames().OrderBy(p => p).ToArray();
        _cmbPort.Items.Clear();
        _cmbPort.Items.AddRange(ports);
        if (_cmbPort.Items.Count > 0 && _cmbPort.SelectedIndex < 0)
            _cmbPort.SelectedIndex = 0;
        UpdateOnlineStatus();
    }

    private void UpdateOnlineStatus()
    {
        bool online = _serial.IsOpen;
        _lblOnline.Text = online ? $"在线: {_serial.PortName}" : "离线";
        _lblOnline.ForeColor = online ? Color.FromArgb(0, 128, 0) : Color.Gray;
    }

    private void TryConnectPort()
    {
        if (_cmbPort.SelectedItem is not string port) return;
        try
        {
            if (_serial.IsOpen && _serial.PortName == port) return;
            _serial.Close();
            _serial.Open(port);
            UpdateOnlineStatus();
            Log($"[OK] 已连接 {port} @ 115200");
        }
        catch (Exception ex)
        {
            UpdateOnlineStatus();
            Log($"[ERR] 连接失败: {ex.Message}", true);
        }
    }

    private void OnSerialLine(string line)
    {
        RunOnUi(() =>
        {
            if (!_isReading) return;
            var parsed = SpdProtocol.ParseHexResponse(line, _deviceType);
            if (parsed == null || parsed.Length == 0) return;
            _spdData = parsed;
            _originalData = (byte[])parsed.Clone();
            _savedData = (byte[])parsed.Clone();
            _deviceType = SpdProtocol.DetectDeviceType(_spdData);
            _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
            UpdateInfoPanel();
            PopulateFieldsFromSpd();
            Log("[OK] 读取成功!");
            _isReading = false;
        });
    }

    private async Task ReadFromChipAsync()
    {
        if (!_serial.IsOpen)
        {
            TryConnectPort();
            if (!_serial.IsOpen) { Log("[ERR] 请先选择有效 COM 端口", true); return; }
        }

        _isReading = true;
        _spdData = [];
        Log("正在读取 SPD 数据...");
        try
        {
            var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdRead, _deviceType, 0);
            _serial.SendCommand(cmd);

            int timeout = 0;
            while (_isReading && timeout < 5000)
            {
                await Task.Delay(100);
                timeout += 100;
            }
            if (_isReading)
            {
                _isReading = false;
                Log("[ERR] 读取超时", true);
            }
        }
        catch (Exception ex)
        {
            _isReading = false;
            Log($"[ERR] 读取失败: {ex.Message}", true);
        }
    }

    private async Task WriteToChipAsync()
    {
        if (!_serial.IsOpen) { Log("[ERR] 设备未连接", true); return; }
        if (_spdData.Length == 0) { Log("[ERR] 无 SPD 数据可写入", true); return; }

        if (!ApplyFieldsToSpd()) return;
        if (!SpdProtocol.DataSizes.TryGetValue(_deviceType, out int size))
            size = _spdData.Length;

        var changes = SpdEditorLogic.GetChanges(_spdData, _savedData, size);
        if (changes.Count == 0) { Log("[WARN] 没有检测到变更"); return; }

        _btnWrite.Enabled = false;
        try
        {
            for (int i = 0; i < changes.Count; i++)
            {
                var (addr, val) = changes[i];
                var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdWrite, _deviceType, addr, val);
                _serial.SendCommand(cmd);
                await Task.Delay(10);
            }
            _savedData = (byte[])_spdData.Clone();
            Log($"[OK] 物理地址 {_i2cAddress:X} 写入成功，共 {changes.Count} 字节。");

            if (_chkBatch.Checked)
            {
                int step = (int)_numStep.Value;
                string newSn = SpdEditorLogic.IncrementSerial(_txtSn.Text, step);
                _txtSn.Text = newSn;
                ApplyFieldsToSpd();
                Log($"[BATCH] SN 自动递增 -> {newSn}");
            }
        }
        catch (Exception ex)
        {
            Log($"[ERR] 写入失败: {ex.Message}", true);
        }
        finally
        {
            _btnWrite.Enabled = true;
        }
    }

    private void LoadBinFile()
    {
        using var dlg = new OpenFileDialog { Filter = "BIN 文件|*.bin|所有文件|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            _spdData = File.ReadAllBytes(dlg.FileName);
            _originalData = (byte[])_spdData.Clone();
            _savedData = [];
            _deviceType = SpdProtocol.DetectDeviceType(_spdData);
            _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
            UpdateInfoPanel();
            PopulateFieldsFromSpd();
            Log($"[OK] 已载入 {Path.GetFileName(dlg.FileName)} ({_spdData.Length} 字节)");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 载入失败: {ex.Message}", true);
        }
    }

    private void BackupBin()
    {
        if (_spdData.Length == 0) { Log("[ERR] 无数据可备份", true); return; }
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
        Directory.CreateDirectory(dir);
        string name = $"SPD_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, _originalData.Length > 0 ? _originalData : _spdData);
        Log($"[OK] 备份成功: {path}");
    }

    private void UnlockSpd() => SendSpdProtectionCommand(SpdProtocol.CmdUnlock, "解锁");

    private void LockSpd() => SendSpdProtectionCommand(SpdProtocol.CmdLock, "锁定");

    private void SendSpdProtectionCommand(byte cmdType, string actionName)
    {
        Log($"[INFO] 发送 SPD {actionName}命令...");
        if (!_serial.IsOpen) { Log("[ERR] 设备未连接", true); return; }
        try
        {
            var cmd = SpdProtocol.BuildCommand(cmdType, _deviceType, 0);
            _serial.SendCommand(cmd);
            Log($"[OK] {actionName}命令已发送");
        }
        catch (Exception ex)
        {
            Log($"[ERR] {actionName}失败: {ex.Message}", true);
        }
    }

    private void PopulateFieldsFromSpd()
    {
        _cmbModuleBrand.SelectedIndex = FindBrandIndex(_spdInfo.ModuleBrand);
        _cmbDieBrand.SelectedIndex = FindBrandIndex(_spdInfo.DieBrand);
        _txtDate.Text = _spdInfo.ProductionDate;
        _txtSn.Text = _spdInfo.SerialNumber;
        _txtModel.Text = _spdInfo.PartNumber;
    }

    private static string FormatProductionDateLabel(string productionDate)
    {
        if (!SpdEditorLogic.TryParseProductionDate(productionDate, out byte yearBcd, out byte weekBcd, out _))
            return productionDate;

        if (yearBcd == 0 && weekBcd == 0)
            return "0000 (未设置)";

        int year = SpdEditorLogic.BcdToInt(yearBcd);
        int week = SpdEditorLogic.BcdToInt(weekBcd);
        return $"{productionDate} (20{year:D2}年第{week}周)";
    }

    private void ReadFieldFromSpd(Action<SpdInfo> applyField)
    {
        if (_spdData.Length == 0)
        {
            MessageBox.Show("请先读取或载入 SPD 数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        applyField(SpdParser.Parse(_spdData, _i2cAddress));
    }

    private int FindBrandIndex(string name)
    {
        for (int i = 0; i < JedecManufacturers.Brands.Length; i++)
        {
            if (name.Contains(JedecManufacturers.Brands[i].Name.Split('(')[0].Trim()) ||
                JedecManufacturers.Brands[i].Name.Contains(name))
                return i;
        }
        return 0;
    }

    private bool ApplyFieldsToSpd()
    {
        if (_spdData.Length == 0) return false;
        _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
        if (!SpdEditorLogic.TryApplyProductionDate(_spdData, _spdInfo, _txtDate.Text, out string dateError))
        {
            MessageBox.Show(
                $"生产日期不符合 SPD 规范:\n{dateError}\n\n格式: YYWW (BCD)\n示例: 2447 = 2024 年第 47 周",
                "校验失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        var modBrand = JedecManufacturers.GetByIndex(_cmbModuleBrand.SelectedIndex);
        var dieBrand = JedecManufacturers.GetByIndex(_cmbDieBrand.SelectedIndex);
        SpdEditorLogic.ApplyModuleBrand(_spdData, _spdInfo, modBrand);
        SpdEditorLogic.ApplyDieBrand(_spdData, _spdInfo, dieBrand);
        SpdEditorLogic.ApplySerialNumber(_spdData, _spdInfo, _txtSn.Text);
        SpdEditorLogic.ApplyPartNumber(_spdData, _spdInfo, _txtModel.Text);
        SpdEditorLogic.UpdateChecksums(_spdData, _spdInfo.MemoryType);
        _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
        _txtDate.Text = _spdInfo.ProductionDate;
        UpdateInfoPanel();
        return true;
    }

    private void UpdateInfoPanel()
    {
        string type = SpdParser.GetTypeLabel(_spdInfo.MemoryType);
        _txtInfo.Text =
            $"类型: {type}  |  地址: {_i2cAddress:X2}{Environment.NewLine}" +
            $"品牌: {_spdInfo.ModuleBrand}{Environment.NewLine}" +
            $"颗粒: {_spdInfo.DieBrand}{Environment.NewLine}" +
            $"型号: {_spdInfo.PartNumber}{Environment.NewLine}" +
            $"SN: {_spdInfo.SerialNumber}  |  日期: {FormatProductionDateLabel(_spdInfo.ProductionDate)}";
    }

    private void Log(string message, bool isError = false)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.SelectionColor = isError ? Color.OrangeRed : Color.LimeGreen;
        _txtLog.AppendText(line);
        _txtLog.ScrollToCaret();
    }

    private void RunOnUi(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    // --- Quick actions ---
    private string RandomModelForSelectedBrand()
    {
        var brand = JedecManufacturers.GetByIndex(_cmbModuleBrand.SelectedIndex);
        int size = _spdInfo.PartNumberSize > 0 ? _spdInfo.PartNumberSize : 20;
        return SpdEditorLogic.RandomPartNumber(brand.Name, size);
    }

    private void QuickRandom()
    {
        _txtSn.Text = SpdEditorLogic.RandomSerial();
        _txtModel.Text = RandomModelForSelectedBrand();
        ApplyFieldsToSpd();
        Log("[OK] 已随机生成型号/SN");
    }

    private void RestoreInitial()
    {
        if (_originalData.Length == 0) { Log("[ERR] 无原始数据", true); return; }
        _spdData = (byte[])_originalData.Clone();
        _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
        PopulateFieldsFromSpd();
        UpdateInfoPanel();
        Log("[OK] 已还原初始状态");
    }

    private void OpenAdvancedEditor()
    {
        if (_spdData.Length == 0)
        {
            Log("[ERR] 请先读取或载入 SPD 数据", true);
            return;
        }
        var data = (byte[])_spdData.Clone();
        if (!AdvancedEditorForm.TryEdit(this, ref data)) return;
        _spdData = data;
        _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);
        UpdateInfoPanel();
        PopulateFieldsFromSpd();
        Log("[OK] 高级 XMP/EXPO/时序/电压参数已更新");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _serial.Dispose();
        base.OnFormClosing(e);
    }
}

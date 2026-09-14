using SpdEditor.Core;

namespace SpdEditor;

/// <summary>
/// 主窗体：串口连接、SPD 读写烧录、参数编辑与日志。
/// </summary>
public partial class MainForm : Form
{
    private readonly SerialDeviceService _serial = new();
    private readonly SpdArduinoDevice _arduino;
    private readonly SmbusSpdService _smbus = new();
    private bool _useArduinoProtocol;
    private bool _useSmbusChannel;
    private byte[] _spdData = [];
    private byte[] _originalData = [];
    private SpdInfo _spdInfo = new();
    private int _deviceType = 4; // 默认 DDR4，与常见读写器一致；可在工具栏切换
    private int _i2cAddress = 0x50;
    private bool _isReading;
    private bool _readPopulateUi = true;
    private TaskCompletionSource<bool>? _readTcs;
    private int _rxBytesDuringRead;

    // Toolbar
    private Panel _toolbarPanel = null!;
    private Label _lblOnline = null!;
    private Label _lblPort = null!;
    private ComboBox _cmbPort = null!;
    private ComboBox _cmbDeviceType = null!;
    private Button _btnOpenPort = null!;
    private Button _btnClosePort = null!;
    private Button _btnRefreshPort = null!;
    private Button _btnRead = null!;
    private Button _btnLoad = null!;
    private Button _btnBackup = null!;
    private Button _btnUnlock = null!;
    private Button _btnLock = null!;

    // Left panel
    private ComboBox _cmbModuleBrand = null!;
    private TextBox _txtDate = null!;
    private TextBox _txtSn = null!;
    private TextBox _txtModel = null!;
    private ComboBox _cmbDieBrand = null!;
    private CheckBox _chkBatch = null!;
    private NumericUpDown _numStep = null!;
    private CheckBox _chkBackupOnBurn = null!;

    // Right panel
    private TextBox _txtInfo = null!;
    private RichTextBox _txtLog = null!;
    private Button _btnWrite = null!;

    public MainForm()
    {
        _arduino = new SpdArduinoDevice(_serial);
        InitializeComponent();
        WireEvents();
        RefreshPorts(selectFirstIfNeeded: true, logResult: false);
        LayoutToolbarButtons();
        Log("系统就绪。串口读写器用 COM 口；本机直读可选端口「SMBus本机」（需管理员）。");
    }

    private void WireEvents()
    {
        _serial.LineReceived += OnSerialLine;
        _serial.RawReceived += chunk =>
        {
            if (!_isReading) return;
            Interlocked.Add(ref _rxBytesDuringRead, chunk.Length);
        };
        _serial.ErrorOccurred += msg => RunOnUi(() => Log($"[ERR] {msg}", true));

        Load += (_, _) =>
        {
            RefreshPorts(selectFirstIfNeeded: true, logResult: false);
            LayoutToolbarButtons();
        };
    }

    private void LayoutToolbarButtons()
    {
        // 三行固定布局：窗口缩放时尺寸与间距不变
        const int padLeft = 12;
        const int btnHeight = 30;
        const int rowGap = 10;
        const int gap = 10;
        const int colWidth = 150;

        int y1 = 10;
        int y2 = y1 + btnHeight + rowGap;
        int y3 = y2 + btnHeight + rowGap;

        int x0 = padLeft;
        int x1 = x0 + colWidth + gap;
        int x2 = x1 + colWidth + gap;
        int x3 = x2 + colWidth + gap;
        int x4 = x3 + colWidth + gap;

        // 第 1 行：在线状态
        _lblOnline.SetBounds(x0, y1, colWidth, btnHeight);

        // 第 2 行：端口标签+COM + 类型 + 打开/关闭/刷新（标签+下拉合计宽度与下方按钮对齐）
        int comboH = Math.Clamp(_cmbPort.PreferredHeight, 24, btnHeight);
        int portLabelW = TextRenderer.MeasureText(_lblPort.Text, _lblPort.Font).Width;
        _lblPort.SetBounds(x0, y2, portLabelW, btnHeight);
        _cmbPort.SetBounds(x0 + portLabelW, y2 + (btnHeight - comboH) / 2, colWidth - portLabelW, comboH);
        _cmbDeviceType.SetBounds(x1, y2 + (btnHeight - comboH) / 2, colWidth, comboH);
        _btnOpenPort.SetBounds(x2, y2, colWidth, btnHeight);
        _btnClosePort.SetBounds(x3, y2, colWidth, btnHeight);
        _btnRefreshPort.SetBounds(x4, y2, colWidth, btnHeight);

        // 第 3 行：读取/载入/保存/解锁/上锁
        _btnRead.SetBounds(x0, y3, colWidth, btnHeight);
        _btnLoad.SetBounds(x1, y3, colWidth, btnHeight);
        _btnBackup.SetBounds(x2, y3, colWidth, btnHeight);
        _btnUnlock.SetBounds(x3, y3, colWidth, btnHeight);
        _btnLock.SetBounds(x4, y3, colWidth, btnHeight);
    }

    private void SyncDeviceTypeFromUi()
    {
        if (_cmbDeviceType.SelectedItem is DeviceTypeItem item)
            _deviceType = item.Type;
    }

    private void SelectDeviceTypeInUi(int type)
    {
        for (int i = 0; i < _cmbDeviceType.Items.Count; i++)
        {
            if (_cmbDeviceType.Items[i] is DeviceTypeItem item && item.Type == type)
            {
                if (_cmbDeviceType.SelectedIndex != i)
                    _cmbDeviceType.SelectedIndex = i;
                return;
            }
        }
    }

    private sealed record DeviceTypeItem(int Type, string Label)
    {
        public override string ToString() => Label;
    }

    private void RefreshPorts(bool selectFirstIfNeeded = true, bool logResult = false)
    {
        string? selected = _cmbPort.SelectedItem as string;
        var ports = SerialDeviceService.GetPortNames().OrderBy(p => p).ToArray();

        // 已连接 SMBus 时刷新通道列表
        if (_smbus.IsConnected)
            _smbus.RescanChannels();

        _cmbPort.Items.Clear();

        // SMBus：已连接则列出各插槽通道，否则保留总入口
        if (_smbus.IsConnected && _smbus.Channels.Count > 0)
        {
            foreach (var ch in _smbus.Channels)
                _cmbPort.Items.Add(ch.DisplayName);
        }
        else
        {
            _cmbPort.Items.Add(SmbusSpdService.ChannelDisplayName);
        }

        _cmbPort.Items.AddRange(ports);

        if (selected != null && _cmbPort.Items.Contains(selected))
            _cmbPort.SelectedItem = selected;
        else if (selectFirstIfNeeded && _smbus.IsConnected && _smbus.Channels.Count > 0)
            _cmbPort.SelectedIndex = 0; // 默认第一条 SMBus 通道
        else if (selectFirstIfNeeded && ports.Length > 0)
            _cmbPort.SelectedIndex = _cmbPort.Items.IndexOf(ports[0]);
        else if (selectFirstIfNeeded && _cmbPort.Items.Count > 0)
            _cmbPort.SelectedIndex = 0;

        SyncChannelFromPortSelection();
        UpdateOnlineStatus();
        if (logResult)
        {
            int smbusCount = _smbus.IsConnected ? _smbus.Channels.Count : 0;
            Log($"[OK] 已刷新端口列表 (SMBus通道 {smbusCount} + COM {ports.Length})");
        }
    }

    private bool IsSmbusPortSelected() =>
        SmbusChannel.IsSmbusDisplayName(_cmbPort.SelectedItem as string);

    private SmbusChannel? GetSelectedSmbusChannel()
    {
        if (_cmbPort.SelectedItem is not string s) return null;
        if (SmbusChannel.TryParse(s, out var ch)) return ch;
        return null;
    }

    private void SyncChannelFromPortSelection()
    {
        _useSmbusChannel = IsSmbusPortSelected();
    }

    private void UpdateOnlineStatus()
    {
        if (_useSmbusChannel && _smbus.IsConnected)
        {
            var ch = GetSelectedSmbusChannel();
            _lblOnline.Text = ch is { } c
                ? $"在线: {c.DisplayName}"
                : $"在线: SMBus/0x{_smbus.LastAddress:X2}";
            _lblOnline.ForeColor = Color.FromArgb(0, 128, 0);
        }
        else
        {
            bool online = _serial.IsOpen;
            _lblOnline.Text = online ? $"在线: {_serial.PortName}" : "离线";
            _lblOnline.ForeColor = online ? Color.FromArgb(0, 128, 0) : Color.Gray;
        }
        LayoutToolbarButtons();
    }

    private void OpenPort()
    {
        SyncChannelFromPortSelection();
        if (_useSmbusChannel)
        {
            OpenSmbusChannel();
            return;
        }

        if (_cmbPort.SelectedItem is not string port)
        {
            Log("[ERR] 请先选择 COM 端口", true);
            return;
        }

        try
        {
            if (_serial.IsOpen && _serial.PortName == port)
            {
                Log($"[INFO] 端口 {port} 已打开");
                return;
            }

            _smbus.Disconnect();
            _serial.Close();
            _useArduinoProtocol = false;
            _serial.Open(port);
            // Arduino 打开串口会复位，需等待 bootloader 结束
            Thread.Sleep(1500);
            UpdateOnlineStatus();
            SyncDeviceTypeFromUi();

            bool probed = _arduino.TryProbe(out string probeDetail);
            if (!probed)
            {
                Thread.Sleep(500);
                probed = _arduino.TryProbe(out probeDetail);
            }

            if (probed)
            {
                _useArduinoProtocol = true;
                Log($"[OK] 已打开 {port} @ 115200 — 识别为 Arduino SPD-RW 固件（1a2m3）");
                Log($"[OK] {probeDetail}");
            }
            else
            {
                _useArduinoProtocol = false;
                Log($"[OK] 已打开 {port} @ 115200 — 使用 spdrw 文本协议（类型 D{_deviceType}）");
                Log($"[INFO] Arduino 探测未通过（{probeDetail}），将按 spdrw 协议通信");
            }
        }
        catch (Exception ex)
        {
            UpdateOnlineStatus();
            Log($"[ERR] 打开端口失败: {ex.Message}", true);
        }
    }

    private void OpenSmbusChannel()
    {
        try
        {
            if (_serial.IsOpen)
            {
                _serial.Close();
                _useArduinoProtocol = false;
            }

            if (_smbus.IsConnected)
            {
                Log("[INFO] SMBus 已连接");
                UpdateOnlineStatus();
                return;
            }

            // PawnIO 签名驱动：未安装则提示安装；已安装则直接打开
            if (!PawnIoInstaller.EnsureInstalled(this, m => Log(m), out string pawnMsg))
            {
                MessageBox.Show(this, pawnMsg, "SMBus / PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateOnlineStatus();
                return;
            }

            Log("[INFO] 正在连接本机 SMBus（需管理员权限）...");
            if (!_smbus.Connect(out string msg))
            {
                Log($"[ERR] {msg}", true);
                MessageBox.Show(this, msg, "SMBus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateOnlineStatus();
                return;
            }

            Log($"[OK] {msg}");
            Log("[INFO] SMBus 模式仅支持读取；烧录/解锁请改用 COM 读写器");
            // 将发现的多条通道填入端口下拉，便于切换插槽
            RefreshPorts(selectFirstIfNeeded: true, logResult: false);
            if (_smbus.Channels.Count > 0)
            {
                _cmbPort.SelectedItem = _smbus.Channels[0].DisplayName;
                SyncChannelFromPortSelection();
            }
            UpdateOnlineStatus();
        }
        catch (Exception ex)
        {
            Log($"[ERR] SMBus 连接失败: {ex.Message}", true);
            UpdateOnlineStatus();
        }
    }

    private void ClosePort()
    {
        try
        {
            SyncChannelFromPortSelection();
            if (_useSmbusChannel || _smbus.IsConnected)
            {
                _smbus.Disconnect();
                RefreshPorts(selectFirstIfNeeded: false, logResult: false);
                UpdateOnlineStatus();
                Log("[OK] 已断开 SMBus");
                if (!_serial.IsOpen)
                    return;
            }

            if (!_serial.IsOpen)
            {
                Log("[INFO] 端口未打开");
                UpdateOnlineStatus();
                return;
            }

            string name = _serial.PortName ?? "";
            _serial.Close();
            _useArduinoProtocol = false;
            UpdateOnlineStatus();
            Log($"[OK] 已关闭 {name}");
        }
        catch (Exception ex)
        {
            UpdateOnlineStatus();
            Log($"[ERR] 关闭端口失败: {ex.Message}", true);
        }
    }

    private void OnSerialLine(string line)
    {
        RunOnUi(() =>
        {
            if (!_isReading) return;
            var parsed = SpdProtocol.ParseHexResponse(line, _deviceType);
            if (parsed == null || parsed.Length == 0) return;

            ApplyReadResult(parsed, _readPopulateUi);
            if (_readPopulateUi)
                Log("[OK] 读取成功!");

            _isReading = false;
            _readTcs?.TrySetResult(true);
        });
    }

    private async Task ReadFromChipAsync()
    {
        SyncChannelFromPortSelection();
        if (_useSmbusChannel)
        {
            await ReadFromSmbusAsync();
            return;
        }

        if (_cmbPort.SelectedItem is not string selectedPort || SmbusChannel.IsSmbusDisplayName(selectedPort))
        {
            Log("[ERR] 请先选择 COM 端口", true);
            return;
        }

        // 未打开，或在线 COM 与菜单选择不一致 → 关闭在线口并打开菜单所选端口
        bool portMismatch = _serial.IsOpen
            && !string.Equals(_serial.PortName, selectedPort, StringComparison.OrdinalIgnoreCase);

        if (!_serial.IsOpen || portMismatch || _smbus.IsConnected)
        {
            if (portMismatch)
                Log($"[INFO] 在线 {_serial.PortName} 与菜单 {selectedPort} 不一致，关闭后切换打开...");
            else if (!_serial.IsOpen)
                Log("[INFO] 端口未打开，自动打开端口...");
            else
                Log("[INFO] 正在切换到菜单所选 COM 端口...");

            OpenPort();
            if (!_serial.IsOpen
                || !string.Equals(_serial.PortName, selectedPort, StringComparison.OrdinalIgnoreCase))
            {
                Log("[ERR] 自动打开端口失败，无法读取 BIN", true);
                return;
            }
        }

        Log("正在读取 SPD 数据...");
        await ReadFromChipCoreAsync(populateUi: true);
    }

    private async Task ReadFromSmbusAsync()
    {
        _btnRead.Enabled = false;
        try
        {
            if (!_smbus.IsConnected)
            {
                Log("[INFO] SMBus 未连接，自动连接...");
                OpenSmbusChannel();
                if (!_smbus.IsConnected)
                    return;
            }

            Log("正在通过 SMBus 读取本机 SPD...");
            var selected = GetSelectedSmbusChannel();
            var (data, detail) = await Task.Run(() =>
            {
                byte[] bytes = _smbus.ReadSpd(selected, out string msg);
                return (bytes, msg);
            });

            if (data.Length == 0)
            {
                Log($"[ERR] {detail}", true);
                return;
            }

            _i2cAddress = _smbus.LastAddress;
            ApplyReadResult(data, populateUi: true);
            // 读完后刷新通道列表（热插拔场景）并保持当前选择
            string keep = GetSelectedSmbusChannel()?.DisplayName
                          ?? $"SMBus#{_smbus.LastBusIndex}/0x{_smbus.LastAddress:X2}";
            _smbus.RescanChannels();
            RefreshPorts(selectFirstIfNeeded: false, logResult: false);
            if (_cmbPort.Items.Contains(keep))
                _cmbPort.SelectedItem = keep;
            SyncChannelFromPortSelection();
            UpdateOnlineStatus();
            Log($"[OK] SMBus 读取成功 — {detail}");
        }
        catch (Exception ex)
        {
            Log($"[ERR] SMBus 读取失败: {ex.Message}", true);
        }
        finally
        {
            _btnRead.Enabled = true;
        }
    }

    /// <summary>向芯片发送读命令并等待完整 SPD 数据。</summary>
    private async Task<bool> ReadFromChipCoreAsync(bool populateUi)
    {
        if (!_serial.IsOpen) return false;

        if (_useArduinoProtocol)
            return await ReadFromArduinoAsync(populateUi);

        return await ReadFromSpdrwAsync(populateUi);
    }

    private async Task<bool> ReadFromArduinoAsync(bool populateUi)
    {
        _btnRead.Enabled = false;
        try
        {
            Log("使用 Arduino 协议扫描并读取...");
            string prepMsg = "";
            bool prepared = await Task.Run(() => _arduino.Prepare(out prepMsg));
            if (!prepared)
            {
                Log($"[ERR] {prepMsg}", true);
                return false;
            }
            Log($"[OK] {prepMsg}");

            var progress = new Progress<int>(p =>
            {
                if (p is 25 or 50 or 75 or 100)
                    Log($"读取进度 {p}%");
            });

            byte[] data = await Task.Run(() => _arduino.ReadAll(progress));
            _i2cAddress = _arduino.I2cAddress;
            ApplyReadResult(data, populateUi);
            if (populateUi)
                Log($"[OK] 读取成功! ({data.Length} 字节 @ 0x{_i2cAddress:X2})");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[ERR] Arduino 读取失败: {ex.Message}", true);
            return false;
        }
        finally
        {
            _btnRead.Enabled = true;
        }
    }

    private void ApplyReadResult(byte[] parsed, bool populateUi)
    {
        _spdData = parsed;
        _originalData = (byte[])parsed.Clone();
        _deviceType = SpdProtocol.DetectDeviceType(_spdData);
        SelectDeviceTypeInUi(_deviceType);
        _spdInfo = SpdParser.Parse(_spdData, _i2cAddress);

        if (populateUi)
        {
            UpdateInfoPanel();
            PopulateFieldsFromSpd();
        }
    }

    private async Task<bool> ReadFromSpdrwAsync(bool populateUi)
    {
        SyncDeviceTypeFromUi();
        _readPopulateUi = populateUi;
        _readTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _isReading = true;
        Interlocked.Exchange(ref _rxBytesDuringRead, 0);

        if (!SpdProtocol.DataSizes.TryGetValue(_deviceType, out int expectBytes))
            expectBytes = 512;

        try
        {
            _serial.BeginReceive(expectBytes);
            var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdRead, _deviceType, 0);
            Log($"发送 spdrw 读命令: D{_deviceType}/{expectBytes}B  {BitConverter.ToString(cmd).Replace("-", " ")}");
            _serial.SendCommand(cmd);

            var delayTask = Task.Delay(10000);
            var finished = await Task.WhenAny(_readTcs.Task, delayTask);
            if (finished != _readTcs.Task)
            {
                _isReading = false;
                _readTcs.TrySetResult(false);
                int rx = Volatile.Read(ref _rxBytesDuringRead);
                string pendingHex = _serial.PeekBufferHex();
                Log("[ERR] 读取超时：设备在时限内未返回完整 SPD", true);
                if (rx == 0 && pendingHex.Length == 0)
                {
                    Log("[HINT] 串口无任何回包。请确认 COM 口、条子插入，或固件协议是否匹配。", true);
                }
                else
                {
                    Log($"[HINT] 已收到原始数据 HEX: {pendingHex}", true);
                    if (pendingHex.StartsWith("26-", StringComparison.Ordinal) || pendingHex.StartsWith("26 ", StringComparison.Ordinal))
                        Log("[HINT] 回包以 0x26('&') 开头，像是 Arduino SPD-RW 固件。请关闭后重新打开端口以自动识别。", true);
                }
                return false;
            }

            return await _readTcs.Task;
        }
        catch (Exception ex)
        {
            _isReading = false;
            _readTcs?.TrySetResult(false);
            Log($"[ERR] 读取失败: {ex.Message}", true);
            return false;
        }
        finally
        {
            _serial.EndReceive();
            _readTcs = null;
            _readPopulateUi = true;
        }
    }

    private async Task WriteToChipAsync()
    {
        SyncChannelFromPortSelection();
        if (_useSmbusChannel)
        {
            const string tip = "SMBus 模式仅支持读取，烧录请改用 COM 串口读写器";
            Log($"[ERR] {tip}", true);
            MessageBox.Show(this, tip, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_serial.IsOpen) { Log("[ERR] 设备未连接", true); return; }

        // 先保存界面设定值（读取芯片后会刷新部分字段）
        int uiBrandIndex = _cmbModuleBrand.SelectedIndex;
        int uiDieIndex = _cmbDieBrand.SelectedIndex;
        string uiDate = _txtDate.Text;
        string uiSn = _txtSn.Text;
        string uiModel = _txtModel.Text;

        _btnWrite.Enabled = false;
        _btnRead.Enabled = false;
        try
        {
            Log("烧录前自动读取当前内存条 SPD...");
            if (!await ReadFromChipCoreAsync(populateUi: false))
            {
                Log("[ERR] 烧录已取消：未能读取当前 SPD", true);
                return;
            }

            // 芯片原始数据（对比 / 备份用）
            byte[] chipOriginal = (byte[])_spdData.Clone();
            var chipInfo = SpdParser.Parse(chipOriginal, _i2cAddress);

            // 恢复界面设定（含颗粒厂家，允许烧录修改）
            _cmbModuleBrand.SelectedIndex = uiBrandIndex;
            _cmbDieBrand.SelectedIndex = uiDieIndex;
            _txtDate.Text = uiDate;
            _txtSn.Text = uiSn;
            _txtModel.Text = uiModel;

            if (!ApplyFieldsToSpd()) return;
            if (!SpdProtocol.DataSizes.TryGetValue(_deviceType, out int size))
                size = _spdData.Length;

            var changes = SpdEditorLogic.GetChanges(_spdData, chipOriginal, size);
            if (changes.Count == 0)
            {
                const string tip = "未修改SPD参数 无需烧录";
                Log($"[WARN] {tip}");
                MessageBox.Show(this, tip, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 烧录前检测写保护：已上锁则提示并中止
            if (await IsSpdLockedBeforeBurnAsync(changes))
                return;

            if (_chkBackupOnBurn.Checked)
            {
                string backupPath = SaveBurnBackupBin(chipOriginal, chipInfo);
                if (backupPath.Length == 0)
                {
                    Log("[ERR] 烧录已取消：原始 SPD 备份失败", true);
                    return;
                }
                Log($"[OK] 原始 SPD 已备份: {backupPath}");
            }
            else
            {
                Log("已跳过烧录前原始 BIN 备份");
            }

            Log($"开始烧录，共 {changes.Count} 字节...");
            byte i2c = (byte)_i2cAddress;
            for (int i = 0; i < changes.Count; i++)
            {
                var (addr, val) = changes[i];
                if (_useArduinoProtocol)
                {
                    bool ok = await Task.Run(() => _arduino.WriteByte(i2c, (ushort)addr, val));
                    if (!ok)
                    {
                        Log("[ERR] SPD已上锁 请解锁后再烧录", true);
                        return;
                    }
                    await Task.Delay(5);
                }
                else
                {
                    var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdWrite, _deviceType, addr, val);
                    _serial.SendCommand(cmd);
                    await Task.Delay(10);
                }
            }
            _originalData = (byte[])_spdData.Clone();
            UpdateInfoPanel();
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
            _btnRead.Enabled = true;
        }
    }

    /// <summary>
    /// 烧录前检测 SPD 写保护。已上锁则提示并返回 true（应中止烧录）。
    /// </summary>
    private async Task<bool> IsSpdLockedBeforeBurnAsync(List<(int Addr, byte Val)> changes)
    {
        const string tip = "SPD已上锁 请解锁后再烧录";
        byte i2c = (byte)_i2cAddress;

        try
        {
            if (!_useArduinoProtocol)
                return false; // spdrw 文本协议无查询接口，交由写入阶段处理

            bool locked = await Task.Run(() =>
            {
                if (_arduino.IsWriteProtected(i2c, _deviceType))
                    return true;
                // 再对首个待写地址做可写性测试，避免漏检
                return changes.Count > 0 && !_arduino.WriteTest(i2c, (ushort)changes[0].Addr);
            });

            if (locked)
            {
                Log($"[ERR] {tip}", true);
                MessageBox.Show(this, tip, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log($"[WARN] 上锁状态检测失败: {ex.Message}，将继续尝试烧录");
            return false;
        }
    }

    /// <summary>
    /// 将刚读取的芯片原始 BIN 保存到程序目录，文件名含 4 位随机号。
    /// </summary>
    private string SaveBurnBackupBin(byte[] data, SpdInfo info)
    {
        try
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string fileName = BuildBinFileName(info, info.PartNumber);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, data);
            return path;
        }
        catch (Exception ex)
        {
            Log($"[ERR] 备份失败: {ex.Message}", true);
            return "";
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
            _deviceType = SpdProtocol.DetectDeviceType(_spdData);
            SelectDeviceTypeInUi(_deviceType);
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
        if (!ApplyFieldsToSpd()) return;

        using var dlg = new SaveFileDialog
        {
            Title = "保存 BIN 文件",
            Filter = "BIN 文件|*.bin|所有文件|*.*",
            FileName = BuildBinFileName(_spdInfo, _txtModel.Text),
            DefaultExt = "bin",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllBytes(dlg.FileName, _spdData);
            Log($"[OK] 已保存: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Log($"[ERR] 保存失败: {ex.Message}", true);
        }
    }

    /// <summary>
    /// 文件名：品牌英文_型号_类型_容量G_年月日时分秒_随机4位.bin
    /// 例：Crucial_CT8G48C40U5_DDR5_8G_260914191530_A3F2.bin
    /// </summary>
    private string BuildBinFileName(SpdInfo info, string? modelOverride)
    {
        string brandEn = SanitizeFileToken(JedecManufacturers.GetEnglishName(info.ModuleBrand));
        string modelSrc = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride : info.PartNumber;
        string model = SanitizeFileToken(string.IsNullOrWhiteSpace(modelSrc) ? "Unknown" : modelSrc.Trim());
        string typePart = SanitizeFileToken(SpdParser.GetTypeLabel(info.MemoryType));

        int capacityGb = info.CapacityGb;
        if (capacityGb <= 0 && _spdData.Length > 0)
            capacityGb = SpdParser.ReadCapacityGb(_spdData, info.MemoryType);
        string sizePart = capacityGb > 0 ? $"{capacityGb}G" : "0G";

        // 年取后两位：yyMMddHHmmss + 4 位随机（保存 BIN / 烧录前备份共用）
        string datePart = DateTime.Now.ToString("yyMMddHHmmss");
        string randPart = RandomAlphanumeric(4);
        return $"{brandEn}_{model}_{typePart}_{sizePart}_{datePart}_{randPart}.bin";
    }

    private static string SanitizeFileToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c =>
        {
            if (invalid.Contains(c) || c is ' ' or '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                return '_';
            return c;
        }).ToArray();
        string cleaned = new string(chars);
        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        cleaned = cleaned.Trim('_');
        return string.IsNullOrEmpty(cleaned) ? "Unknown" : cleaned;
    }

    private static string RandomAlphanumeric(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, static (span, alphabet) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        });
    }

    private void UnlockSpd()
    {
        SyncChannelFromPortSelection();
        if (_useSmbusChannel)
        {
            Log("[ERR] SMBus 模式不支持解锁，请使用 COM 读写器", true);
            return;
        }
        Log("[INFO] 发送 SPD 解锁命令...");
        if (!_serial.IsOpen) { Log("[ERR] 设备未连接", true); return; }
        try
        {
            if (_useArduinoProtocol)
            {
                bool ok = _arduino.ClearRswp((byte)_i2cAddress);
                Log(ok ? "[OK] RSWP 已清除（解锁）" : "[ERR] 解锁失败", !ok);
            }
            else
            {
                var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdUnlock, _deviceType, 0);
                _serial.SendCommand(cmd);
                Log("[OK] 解锁命令已发送");
            }
        }
        catch (Exception ex)
        {
            Log($"[ERR] 解锁失败: {ex.Message}", true);
        }
    }

    private void LockSpd()
    {
        SyncChannelFromPortSelection();
        if (_useSmbusChannel)
        {
            Log("[ERR] SMBus 模式不支持上锁，请使用 COM 读写器", true);
            return;
        }
        Log("[INFO] 发送 SPD 上锁命令...");
        if (!_serial.IsOpen) { Log("[ERR] 设备未连接", true); return; }
        try
        {
            if (_useArduinoProtocol)
            {
                // DDR4 通常 4 个 RSWP 块；逐块启用
                int blocks = _deviceType >= 5 ? 16 : _deviceType >= 4 ? 4 : 1;
                for (byte b = 0; b < blocks; b++)
                {
                    if (!_arduino.SetRswp((byte)_i2cAddress, b))
                    {
                        Log($"[ERR] 锁定块 {b} 失败", true);
                        return;
                    }
                }
                Log($"[OK] 已锁定 {blocks} 个 RSWP 块");
            }
            else
            {
                var cmd = SpdProtocol.BuildCommand(SpdProtocol.CmdLock, _deviceType, 0);
                _serial.SendCommand(cmd);
                Log("[OK] 锁定命令已发送");
            }
        }
        catch (Exception ex)
        {
            Log($"[ERR] 锁定失败: {ex.Message}", true);
        }
    }

    private void PopulateFieldsFromSpd()
    {
        _cmbModuleBrand.SelectedIndex = JedecManufacturers.FindModuleIndexByName(_spdInfo.ModuleBrand);
        _cmbDieBrand.SelectedIndex = JedecManufacturers.FindDieIndexByName(_spdInfo.DieBrand);
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

        var modBrand = JedecManufacturers.GetModuleByIndex(_cmbModuleBrand.SelectedIndex);
        SpdEditorLogic.ApplyModuleBrand(_spdData, _spdInfo, modBrand);
        var dieBrand = JedecManufacturers.GetDieByIndex(_cmbDieBrand.SelectedIndex);
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
        int capacityGb = _spdInfo.CapacityGb;
        if (capacityGb <= 0 && _spdData.Length > 0)
            capacityGb = SpdParser.ReadCapacityGb(_spdData, _spdInfo.MemoryType);
        string capacityText = capacityGb > 0 ? $"{capacityGb}G" : "-";

        _txtInfo.Text =
            $"类型: {type}  |  地址: {_i2cAddress:X2}{Environment.NewLine}" +
            $"品牌: {_spdInfo.ModuleBrand}  |  {_spdInfo.FrequencyLabel}  |  {_spdInfo.TimingLabel}{Environment.NewLine}" +
            $"颗粒: {_spdInfo.DieBrand}  |  内存容量 {capacityText}{Environment.NewLine}" +
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

    private string RandomModelForSelectedBrand()
    {
        var brand = JedecManufacturers.GetModuleByIndex(_cmbModuleBrand.SelectedIndex);
        int size = _spdInfo.PartNumberSize > 0 ? _spdInfo.PartNumberSize : 20;
        return SpdEditorLogic.RandomPartNumber(brand.Name, size);
    }

    private void QuickRandom()
    {
        if (_spdData.Length == 0)
        {
            Log("[ERR] 请先读取或载入 SPD 数据", true);
            return;
        }

        // 随机品牌 / 颗粒厂家（跳过「未知」项）
        int brandCount = JedecManufacturers.ModuleBrands.Length;
        if (brandCount > 1)
            _cmbModuleBrand.SelectedIndex = Random.Shared.Next(1, brandCount);

        int dieCount = JedecManufacturers.DieManufacturers.Length;
        if (dieCount > 1)
            _cmbDieBrand.SelectedIndex = Random.Shared.Next(1, dieCount);

        _txtDate.Text = SpdEditorLogic.RandomProductionDate();
        _txtSn.Text = SpdEditorLogic.RandomSerial();
        _txtModel.Text = RandomModelForSelectedBrand();

        if (!ApplyFieldsToSpd()) return;
        Log("[OK] 已一键随机：品牌 / 颗粒厂家 / 生产日期 / 型号 / 序列号");
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _smbus.Dispose();
        _serial.Dispose();
        base.OnFormClosing(e);
    }
}

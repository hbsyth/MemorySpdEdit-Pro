using System.Security.Principal;
using System.Text.RegularExpressions;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.SPD;
using RAMSPDToolkit.SPD.Enums;
using RAMSPDToolkit.SPD.Interop.Shared;
using RAMSPDToolkit.Windows.Driver;
using RAMSPDToolkit.Windows.Driver.Implementations;

namespace SpdEditor.Core;

/// <summary>SMBus 上发现的一条 SPD 通道（总线号 + 从地址）。</summary>
public readonly record struct SmbusChannel(int BusIndex, byte Address)
{
    /// <summary>下拉显示名，例如 SMBus#0/0x51</summary>
    public string DisplayName => $"SMBus#{BusIndex}/0x{Address:X2}";

    public static bool TryParse(string? text, out SmbusChannel channel)
    {
        channel = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Regex.Match(text.Trim(), @"^SMBus#(\d+)/0x([0-9A-Fa-f]{2})$");
        if (!m.Success) return false;
        channel = new SmbusChannel(int.Parse(m.Groups[1].Value), Convert.ToByte(m.Groups[2].Value, 16));
        return true;
    }

    public static bool IsSmbusDisplayName(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Equals(SmbusSpdService.ChannelDisplayName, StringComparison.Ordinal) ||
         text.StartsWith("SMBus#", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 通过主板 SMBus 只读本机内存条 SPD（PawnIO / WinRing0）。
/// 烧录/解锁请仍使用串口读写器。
/// </summary>
public sealed class SmbusSpdService : IDisposable
{
    /// <summary>端口列表中的总入口（连接后会展开为各通道）。</summary>
    public const string ChannelDisplayName = "SMBus本机";

    private bool _driverLoaded;
    private bool _disposed;
    private List<SmbusChannel> _channels = [];
    private readonly Dictionary<(int Bus, byte Addr), SPDAccessor> _accessors = new();

    public bool IsConnected => _driverLoaded && SMBusManager.RegisteredSMBuses.Count > 0;
    public byte LastAddress { get; private set; } = 0x50;
    public int LastBusIndex { get; private set; }
    public IReadOnlyList<SmbusChannel> Channels => _channels;

    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>加载硬件访问驱动、枚举 SMBus，并扫描所有 SPD 通道。</summary>
    public bool Connect(out string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            message = "SMBus 读取仅支持 Windows";
            return false;
        }

        if (!IsAdministrator())
        {
            message = "SMBus 读取需要管理员权限，请右键以管理员身份运行本程序";
            return false;
        }

        string? oldCwd = null;
        try
        {
            // WinRing0 会把 .sys/.dll 解压到「当前工作目录」；固定到程序目录避免权限/路径问题
            oldCwd = Environment.CurrentDirectory;
            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(appDir))
                Environment.CurrentDirectory = appDir;

            // 依次尝试驱动：优先能扫到 SPD 通道的配置；PawnIO（签名）优先，再 WinRing0
            var errors = new List<string>();
            foreach (var impl in new[] { DriverImplementation.PawnIO, DriverImplementation.WinRing0 })
            {
                bool[] wmiModes = impl == DriverImplementation.WinRing0 ? [false, true] : [false];
                foreach (bool useWmi in wmiModes)
                {
                    if (!TryLoadDriver(impl, out string loadDetail))
                    {
                        errors.Add(loadDetail);
                        break; // 该驱动无法加载，换下一个驱动
                    }

                    try
                    {
                        SMBusManager.UseWMI = useWmi;
                        SMBusManager.DetectSMBuses();
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{impl} Detect 异常: {ex.Message}");
                        continue;
                    }

                    int busCount = SMBusManager.RegisteredSMBuses.Count;
                    _channels = ScanChannels();
                    _driverLoaded = true;

                    if (_channels.Count > 0)
                    {
                        message =
                            $"已连接 SMBus（驱动 {impl}" +
                            (impl == DriverImplementation.WinRing0 ? $", UseWMI={useWmi}" : "") +
                            $"，总线 {busCount}），发现 {_channels.Count} 条通道：" +
                            string.Join(", ", _channels.Select(c => c.DisplayName));
                        return true;
                    }

                    errors.Add(
                        $"{impl}" +
                        (impl == DriverImplementation.WinRing0 ? $"/WMI={useWmi}" : "") +
                        $": 总线 {busCount}，SPD 通道 0");
                }
            }

            _channels = [];
            _accessors.Clear();
            SafeUnloadDriver();
            _driverLoaded = false;

            message =
                "未能通过 SMBus 发现内存 SPD（地址 0x50–0x57）。\n\n" +
                $"探测摘要：{string.Join("；", errors)}\n\n" +
                "常见原因：\n" +
                "1. 笔记本「板载/焊接」LPDDR 往往不经标准 SMBus 暴露 SPD，软件无法直读\n" +
                "2. 未以管理员运行，或 PawnIO / WinRing0 驱动被「内存完整性」拦截\n" +
                "3. BIOS 关闭了 SPD 访问，或 IMC/PCH 总线被其它监控软件占用\n" +
                "4. 可插拔 SODIMM/UDIMM 请确认内存已识别，并关闭 HWiNFO 等占用 SMBus 的工具后重试";
            return false;
        }
        catch (Exception ex)
        {
            message = $"SMBus 连接失败: {ex.Message}";
            return false;
        }
        finally
        {
            if (oldCwd != null)
            {
                try { Environment.CurrentDirectory = oldCwd; } catch { /* ignore */ }
            }
        }
    }

    private static bool TryLoadDriver(DriverImplementation impl, out string detail)
    {
        try { DriverManager.UnloadDriver(); } catch { /* ignore */ }

        try
        {
            bool ok = DriverManager.LoadDriver(impl);
            if (ok && DriverManager.Driver is { IsOpen: true })
            {
                detail = impl.ToString();
                return true;
            }

            detail = ok ? $"{impl} Load 成功但 IsOpen=false" : $"{impl} Load 返回失败";
        }
        catch (Exception ex)
        {
            detail = $"{impl} 异常: {ex.Message}";
        }

        try { DriverManager.UnloadDriver(); } catch { /* ignore */ }
        return false;
    }

    private static void SafeUnloadDriver()
    {
        try { DriverManager.UnloadDriver(); } catch { /* ignore */ }
    }

    /// <summary>重新扫描插槽通道（驱动需已连接）。</summary>
    public IReadOnlyList<SmbusChannel> RescanChannels()
    {
        if (!IsConnected) return [];
        _channels = ScanChannels();
        return _channels;
    }

    private List<SmbusChannel> ScanChannels()
    {
        var found = new List<SmbusChannel>();
        _accessors.Clear();

        for (int bi = 0; bi < SMBusManager.RegisteredSMBuses.Count; bi++)
        {
            var bus = SMBusManager.RegisteredSMBuses[bi];
            for (byte addr = SPDConstants.SPD_BEGIN; addr <= SPDConstants.SPD_END; addr++)
            {
                try
                {
                    if (!TryResolveAccessor(bus, addr, out var accessor))
                        continue;

                    var key = (bi, addr);
                    _accessors[key] = accessor;
                    found.Add(new SmbusChannel(bi, addr));
                }
                catch
                {
                    // 无设备或总线忙
                }
            }
        }

        return found;
    }

    // JEDEC / RAMSPDToolkit 内部常量（原类型为 internal，这里本地镜像）
    private const byte Ddr5Mr11VirtualPage = 0x0B;
    private const byte Ddr5DeviceTypeMost = 0x00;
    private const byte Ddr5DeviceTypeLeast = 0x01;
    private const byte Ddr5MagicMost = 0x51;
    private const byte Ddr5MagicLeast = 0x18;
    private const byte Ddr4Spa0Address = 0x36;
    private const byte Ddr4Page0Data = 0x00;
    private const byte Ddr4MemoryTypeOffset = 0x02;

    /// <summary>
    /// 先走官方 SPDDetector；失败时按类型字节/探针强制创建 DDR4/DDR5/DDR3 访问器。
    /// （DDR5 官方 IsAvailable 依赖温度传感器魔术字，部分模组会漏检。）
    /// </summary>
    private static bool TryResolveAccessor(SMBusInterface bus, byte addr, out SPDAccessor accessor)
    {
        accessor = null!;

        try
        {
            var detector = new SPDDetector(bus, addr);
            if (detector.IsValid && detector.Accessor != null)
            {
                accessor = detector.Accessor;
                return true;
            }
        }
        catch
        {
            // fall through
        }

        // --- 回退探针 ---
        // DDR5：切页 0 后读 0x82（页内偏移|0x80）或魔术字 0x51/0x18
        try
        {
            bus.i2c_smbus_write_byte_data(addr, Ddr5Mr11VirtualPage, 0);
            Thread.Sleep(SPDConstants.SPD_IO_DELAY);

            int typeAt82 = bus.i2c_smbus_read_byte_data(addr, 0x82);
            int magicHi = bus.i2c_smbus_read_byte_data(addr, Ddr5DeviceTypeMost);
            int magicLo = bus.i2c_smbus_read_byte_data(addr, Ddr5DeviceTypeLeast);
            bool looksDdr5 =
                typeAt82 == (int)SPDMemoryType.SPD_DDR5_SDRAM ||
                typeAt82 == (int)SPDMemoryType.SPD_LPDDR5_SDRAM ||
                (magicHi == Ddr5MagicMost && magicLo == Ddr5MagicLeast);

            if (looksDdr5)
            {
                accessor = new DDR5Accessor(bus, addr);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        // DDR4：SPA0 切页后读类型字节
        try
        {
            bus.i2c_smbus_write_byte_data(Ddr4Spa0Address, Ddr4Page0Data, 0xFF);
            Thread.Sleep(SPDConstants.SPD_IO_DELAY);
            int type = bus.i2c_smbus_read_byte_data(addr, Ddr4MemoryTypeOffset);
            if (type is (int)SPDMemoryType.SPD_DDR4_SDRAM
                or (int)SPDMemoryType.SPD_DDR4E_SDRAM
                or (int)SPDMemoryType.SPD_LPDDR4_SDRAM
                or (int)SPDMemoryType.SPD_LPDDR4X_SDRAM)
            {
                accessor = new DDR4Accessor(bus, addr);
                return true;
            }

            if (type is (int)SPDMemoryType.SPD_DDR3_SDRAM or (int)SPDMemoryType.SPD_LPDDR3_SDRAM)
            {
                accessor = new DDR3Accessor(bus, addr);
                return true;
            }

            if (type is (int)SPDMemoryType.SPD_DDR5_SDRAM or (int)SPDMemoryType.SPD_LPDDR5_SDRAM)
            {
                accessor = new DDR5Accessor(bus, addr);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    /// <summary>按指定通道读取完整 SPD；channel 为空则读第一条发现的通道。</summary>
    public byte[] ReadSpd(SmbusChannel? channel, out string detail)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SMBus 未连接");

        if (_channels.Count == 0)
            _channels = ScanChannels();

        if (_channels.Count == 0)
        {
            detail = "SMBus 上未发现 SPD 设备（0x50–0x57）。焊接板载内存通常无法通过 SMBus 读取。";
            return [];
        }

        SmbusChannel pick = channel is { } c && _channels.Contains(c)
            ? c
            : _channels[0];

        if (pick.BusIndex < 0 || pick.BusIndex >= SMBusManager.RegisteredSMBuses.Count)
        {
            detail = $"通道 {pick.DisplayName} 总线索引无效";
            return [];
        }

        var key = (pick.BusIndex, pick.Address);
        if (!_accessors.TryGetValue(key, out var accessor) || accessor == null)
        {
            var bus = SMBusManager.RegisteredSMBuses[pick.BusIndex];
            if (!TryResolveAccessor(bus, pick.Address, out accessor))
            {
                detail = $"通道 {pick.DisplayName} 无效或已移除";
                return [];
            }
            _accessors[key] = accessor;
        }

        LastBusIndex = pick.BusIndex;
        LastAddress = pick.Address;

        int size = SpdSizeFor(accessor.MemoryType());
        var data = DumpBytes(accessor, size);
        detail = $"{pick.DisplayName}，读出 {data.Length} 字节（可选通道共 {_channels.Count} 条）";
        return data;
    }

    private static int SpdSizeFor(SPDMemoryType type) => type switch
    {
        SPDMemoryType.SPD_DDR5_SDRAM or SPDMemoryType.SPD_LPDDR5_SDRAM => 1024,
        SPDMemoryType.SPD_DDR4_SDRAM or SPDMemoryType.SPD_DDR4E_SDRAM
            or SPDMemoryType.SPD_LPDDR4_SDRAM or SPDMemoryType.SPD_LPDDR4X_SDRAM => 512,
        _ => 256,
    };

    /// <summary>
    /// 导出完整 SPD。优先按页内 SMBus 块读（≤32 字节）；
    /// 若块读疑似失败（短读/关键区全 0），再对该段逐字节补读。
    /// </summary>
    private static byte[] DumpBytes(SPDAccessor accessor, int size)
    {
        var data = new byte[size];
        const int pageSize = 128;
        const int maxBlock = 32;

        int offset = 0;
        while (offset < size)
        {
            int pageRemain = pageSize - (offset % pageSize);
            int len = Math.Min(maxBlock, Math.Min(size - offset, pageRemain));
            ReadChunk(accessor, data, offset, len);
            offset += len;
        }

        RepairSuspectRanges(accessor, data);
        return data;
    }

    private static void ReadChunk(SPDAccessor accessor, byte[] data, int offset, int len)
    {
        byte[] part;
        try
        {
            part = accessor.At((ushort)offset, (byte)len);
        }
        catch
        {
            part = [];
        }

        if (part.Length >= len)
        {
            Buffer.BlockCopy(part, 0, data, offset, len);
            return;
        }

        if (part.Length > 0)
            Buffer.BlockCopy(part, 0, data, offset, part.Length);

        for (int i = part.Length; i < len; i++)
            data[offset + i] = accessor.At((ushort)(offset + i));
    }

    private static void RepairSuspectRanges(SPDAccessor accessor, byte[] data)
    {
        if (data.Length > 37
            && (data[30] != 0 || data[31] != 0)
            && data[32] == 0 && data[33] == 0 && data[34] == 0 && data[35] == 0
            && data[36] == 0 && data[37] == 0)
        {
            for (ushort i = 32; i < 64 && i < data.Length; i++)
                data[i] = accessor.At(i);
        }

        if (data.Length > 235
            && data[4] != 0
            && data[234] == 0 && data[235] == 0)
        {
            for (ushort i = 224; i < 256 && i < data.Length; i++)
                data[i] = accessor.At(i);
        }
    }

    public void Disconnect()
    {
        if (!_driverLoaded) return;
        try
        {
            DriverManager.UnloadDriver();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _driverLoaded = false;
            _channels = [];
            _accessors.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}

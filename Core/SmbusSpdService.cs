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
/// 通过主板 SMBus（WinRing0）只读本机内存条 SPD。
/// 烧录/解锁请仍使用串口读写器。
/// </summary>
public sealed class SmbusSpdService : IDisposable
{
    /// <summary>端口列表中的总入口（连接后会展开为各通道）。</summary>
    public const string ChannelDisplayName = "SMBus本机";

    private bool _driverLoaded;
    private bool _disposed;
    private List<SmbusChannel> _channels = [];

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

            if (!_driverLoaded)
            {
                if (!TryLoadHardwareDriver(out string driverDetail))
                {
                    message =
                        "硬件访问驱动加载失败（管理员已就绪，但驱动仍被系统拒绝）。\n\n" +
                        $"详情：{driverDetail}\n\n" +
                        "请依次排查：\n" +
                        "1. Windows 安全中心 → 设备安全性 → 核心隔离 → 关闭「内存完整性」后重启\n" +
                        "2. 暂时退出杀毒/安全软件对 .sys 驱动的拦截\n" +
                        "3. 若开启了 Secure Boot，WinRing0 旧驱动常无法加载；可安装 PawnIO 后再试\n" +
                        "   （https://github.com/namazso/PawnIO/releases）\n" +
                        $"4. 确认程序目录可写：{appDir}";
                    return false;
                }
                _driverLoaded = true;
            }

            SMBusManager.DetectSMBuses();
            if (SMBusManager.RegisteredSMBuses.Count == 0)
            {
                _channels = [];
                message = "驱动已加载，但未检测到可用 SMBus 控制器";
                return false;
            }

            _channels = ScanChannels();
            if (_channels.Count == 0)
            {
                message = $"已连接 SMBus（{SMBusManager.RegisteredSMBuses.Count} 条总线），但未发现 SPD（0x50–0x57）";
                return true;
            }

            message = $"已连接 SMBus（驱动 {DriverManager.DriverImplementation}），发现 {_channels.Count} 条通道：" +
                      string.Join(", ", _channels.Select(c => c.DisplayName));
            return true;
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

    /// <summary>优先 PawnIO（较新），失败再试 WinRing0。</summary>
    private static bool TryLoadHardwareDriver(out string detail)
    {
        var errors = new List<string>();

        foreach (var impl in new[] { DriverImplementation.PawnIO, DriverImplementation.WinRing0 })
        {
            try
            {
                DriverManager.UnloadDriver();
            }
            catch { /* ignore */ }

            try
            {
                bool ok = DriverManager.LoadDriver(impl);
                if (ok && DriverManager.Driver is { IsOpen: true })
                {
                    detail = impl.ToString();
                    return true;
                }

                string why = ok
                    ? $"{impl} Load 返回成功但 IsOpen=false"
                    : $"{impl} Load 返回失败";
                errors.Add(why);
            }
            catch (Exception ex)
            {
                errors.Add($"{impl} 异常: {ex.Message}");
            }

            try { DriverManager.UnloadDriver(); } catch { /* ignore */ }
        }

        detail = string.Join("；", errors);
        return false;
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
        for (int bi = 0; bi < SMBusManager.RegisteredSMBuses.Count; bi++)
        {
            var bus = SMBusManager.RegisteredSMBuses[bi];
            for (byte addr = SPDConstants.SPD_BEGIN; addr <= SPDConstants.SPD_END; addr++)
            {
                try
                {
                    var detector = new SPDDetector(bus, addr);
                    if (!detector.IsValid || detector.Accessor == null)
                        continue;
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

    /// <summary>按指定通道读取完整 SPD；channel 为空则读第一条发现的通道。</summary>
    public byte[] ReadSpd(SmbusChannel? channel, out string detail)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SMBus 未连接");

        if (_channels.Count == 0)
            _channels = ScanChannels();

        if (_channels.Count == 0)
        {
            detail = "SMBus 上未发现 SPD 设备（0x50–0x57）";
            return [];
        }

        SmbusChannel pick = channel is { } c && _channels.Contains(c)
            ? c
            : _channels[0];

        var bus = SMBusManager.RegisteredSMBuses[pick.BusIndex];
        var detector = new SPDDetector(bus, pick.Address);
        if (!detector.IsValid || detector.Accessor == null)
        {
            detail = $"通道 {pick.DisplayName} 无效或已移除";
            return [];
        }

        LastBusIndex = pick.BusIndex;
        LastAddress = pick.Address;

        int size = SpdSizeFor(detector.SPDMemoryType);
        var data = DumpBytes(detector.Accessor, size);
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
    /// 导出完整 SPD。优先按页内 SMBus 块读（≤32 字节），比逐字节快约 30 倍；
    /// 若块读疑似失败（短读/关键区全 0），再对该段逐字节补读。
    /// </summary>
    private static byte[] DumpBytes(SPDAccessor accessor, int size)
    {
        var data = new byte[size];
        // DDR5 页 128；DDR4 页 256。用 128 对齐对两者都安全。
        const int pageSize = 128;
        // I2C_SMBUS_BLOCK_MAX 通常为 32；超过会导致后半段被填 0
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

        // 短读或失败：该段改逐字节（仍会正确切页）
        for (int i = part.Length; i < len; i++)
            data[offset + i] = accessor.At((ushort)(offset + i));
    }

    /// <summary>
    /// 检测「前半块有数据、后半关键区全 0」的典型块读失败，并逐字节修补。
    /// </summary>
    private static void RepairSuspectRanges(SPDAccessor accessor, byte[] data)
    {
        // DDR5：tAAmin(30-31) 有值但 tRCD/tRP/tRAS(32-37) 全 0 → 修补 32..63
        if (data.Length > 37
            && (data[30] != 0 || data[31] != 0)
            && data[32] == 0 && data[33] == 0 && data[34] == 0 && data[35] == 0
            && data[36] == 0 && data[37] == 0)
        {
            for (ushort i = 32; i < 64 && i < data.Length; i++)
                data[i] = accessor.At(i);
        }

        // DDR5：模组组织/总线宽(234-235) 全 0 且密度字节非 0 → 修补 224..255
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
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}

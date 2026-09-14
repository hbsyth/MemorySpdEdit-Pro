using System.IO.Ports;
using System.Text;

namespace SpdEditor.Core;

/// <summary>
/// 串口通信服务：支持 spdrw 文本协议（事件收包）与 Arduino 二进制事务（同步收发）。
/// </summary>
public sealed class SerialDeviceService : IDisposable
{
    private SerialPort? _port;
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();
    private readonly object _ioLock = new();
    private int _expectedHexChars;

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;

    /// <summary>按行（以 \n 结尾）收到文本时触发（spdrw 文本协议）。</summary>
    public event Action<string>? LineReceived;

    /// <summary>任意原始接收片段（调试用，仅事件驱动模式）。</summary>
    public event Action<string>? RawReceived;

    public event Action<string>? ErrorOccurred;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Open(string portName, int baudRate = 115200)
    {
        Close();
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 1000,
            WriteTimeout = 3000,
            NewLine = "\n",
            Encoding = Encoding.ASCII,
            ReceivedBytesThreshold = 1,
        };
        _port.DataReceived += OnDataReceived;
        _port.Open();
        try
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        catch { /* ignore */ }
        lock (_lock) _buffer.Clear();
    }

    public void Close()
    {
        if (_port == null) return;
        try
        {
            _port.DataReceived -= OnDataReceived;
            if (_port.IsOpen) _port.Close();
        }
        catch { /* ignore */ }
        finally
        {
            _port.Dispose();
            _port = null;
            lock (_lock)
            {
                _buffer.Clear();
                _expectedHexChars = 0;
            }
        }
    }

    public void BeginReceive(int expectedByteCount = 0)
    {
        lock (_lock)
        {
            _buffer.Clear();
            _expectedHexChars = Math.Max(0, expectedByteCount) * 2;
        }
        try
        {
            if (_port?.IsOpen == true)
                _port.DiscardInBuffer();
        }
        catch { /* ignore */ }
    }

    public void EndReceive()
    {
        lock (_lock) _expectedHexChars = 0;
    }

    /// <summary>当前接收缓冲的原始字节预览（十六进制，超时诊断用）。</summary>
    public string PeekBufferHex()
    {
        lock (_lock)
        {
            string s = _buffer.ToString();
            if (s.Length == 0) return "";
            var bytes = Encoding.Latin1.GetBytes(s);
            return BitConverter.ToString(bytes);
        }
    }

    public void SendCommand(byte[] cmd)
    {
        if (_port?.IsOpen != true)
            throw new InvalidOperationException("串口未连接");
        _port.Write(cmd, 0, cmd.Length);
    }

    /// <summary>
    /// 同步收发 1a2m3 Arduino 二进制包。发送命令后等待 '&'+len+body+checksum。
    /// </summary>
    public byte[] TransactArduino(byte[] command, int timeoutMs = 5000)
    {
        if (_port?.IsOpen != true)
            throw new InvalidOperationException("串口未连接");

        lock (_ioLock)
        {
            _port.DataReceived -= OnDataReceived;
            try
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }
                catch { /* ignore */ }
                lock (_lock) _buffer.Clear();

                _port.Write(command, 0, command.Length);
                _port.BaseStream.Flush();

                long deadline = Environment.TickCount64 + timeoutMs;
                var packet = new List<byte>(SpdArduinoProtocol.PacketMaxSize);

                while (Environment.TickCount64 < deadline)
                {
                    int b = ReadByteUntil(deadline);
                    if (b < 0) break;

                    if (packet.Count == 0)
                    {
                        if (b == SpdArduinoProtocol.HeaderAlert)
                        {
                            ReadByteUntil(deadline); // 丢弃告警类型
                            continue;
                        }
                        if (b != SpdArduinoProtocol.HeaderResponse)
                            continue;
                    }

                    packet.Add((byte)b);
                    if (packet.Count < 2) continue;

                    int len = packet[1];
                    if (len > SpdArduinoProtocol.MaxBodySize)
                    {
                        packet.Clear();
                        continue;
                    }

                    int need = SpdArduinoProtocol.PacketMinSize + len + 1;
                    if (packet.Count < need) continue;

                    if (!SpdArduinoProtocol.TryParsePacket(CollectionsMarshalAsSpan(packet), out var body))
                        throw new InvalidDataException("Arduino 回包校验失败");
                    return body;
                }

                throw new TimeoutException("Arduino 回包超时");
            }
            finally
            {
                if (_port?.IsOpen == true)
                    _port.DataReceived += OnDataReceived;
            }
        }
    }

    private int ReadByteUntil(long deadlineMs)
    {
        while (Environment.TickCount64 < deadlineMs)
        {
            if (_port == null || !_port.IsOpen) return -1;
            if (_port.BytesToRead > 0)
                return _port.ReadByte();
            Thread.Sleep(2);
        }
        return -1;
    }

    private static ReadOnlySpan<byte> CollectionsMarshalAsSpan(List<byte> list)
        => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port?.IsOpen != true) return;
            // 二进制事务期间已卸载事件；此处仅处理文本协议
            string chunk = _port.ReadExisting();
            if (chunk.Length == 0) return;

            RawReceived?.Invoke(chunk);

            lock (_lock)
            {
                _buffer.Append(chunk);
                DrainLinesAndPayload();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    private void DrainLinesAndPayload()
    {
        while (true)
        {
            int idx = _buffer.ToString().IndexOf('\n');
            if (idx < 0) break;
            string line = _buffer.ToString(0, idx).Trim('\r');
            _buffer.Remove(0, idx + 1);
            if (line.Length > 0)
                LineReceived?.Invoke(line);
        }

        if (_expectedHexChars > 0)
        {
            string pending = _buffer.ToString();
            int hexCount = CountHexDigits(pending);
            if (hexCount >= _expectedHexChars)
            {
                string line = ExtractLeadingHex(pending, _expectedHexChars);
                _buffer.Clear();
                if (line.Length > 0)
                    LineReceived?.Invoke(line);
            }
        }
    }

    private static int CountHexDigits(string s)
    {
        int n = 0;
        foreach (char c in s)
        {
            if (Uri.IsHexDigit(c)) n++;
        }
        return n;
    }

    private static string ExtractLeadingHex(string s, int hexChars)
    {
        var sb = new StringBuilder(hexChars);
        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c)) continue;
            sb.Append(c);
            if (sb.Length >= hexChars) break;
        }
        return sb.ToString();
    }

    public void Dispose() => Close();
}

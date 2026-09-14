using System.IO.Ports;
using System.Text;

namespace SpdEditor.Core;

public sealed class SerialDeviceService : IDisposable
{
    private SerialPort? _port;
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;

    public event Action<string>? LineReceived;
    public event Action<string>? ErrorOccurred;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Open(string portName, int baudRate = 115200)
    {
        Close();
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 500,
            WriteTimeout = 3000,
            NewLine = "\n",
        };
        _port.DataReceived += OnDataReceived;
        _port.Open();
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
            lock (_lock) _buffer.Clear();
        }
    }

    public void SendCommand(byte[] cmd)
    {
        if (_port?.IsOpen != true)
            throw new InvalidOperationException("串口未连接");
        _port.Write(cmd, 0, cmd.Length);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port?.IsOpen != true) return;
            string chunk = _port.ReadExisting();
            lock (_lock)
            {
                _buffer.Append(chunk);
                while (true)
                {
                    int idx = _buffer.ToString().IndexOf('\n');
                    if (idx < 0) break;
                    string line = _buffer.ToString(0, idx).Trim('\r');
                    _buffer.Remove(0, idx + 1);
                    if (line.Length > 0)
                        LineReceived?.Invoke(line);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public void Dispose() => Close();
}

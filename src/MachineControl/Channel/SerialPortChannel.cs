using System.IO.Ports;
using System.Text;

namespace MachineControl.Channel;

/// <summary>
/// System.IO.Ports 的 ISerialChannel 实现。
/// 读超时 1s / 写超时 2s；此处 ReadTimeout 仅作底层兜底，上层用轮询 + 累积缓冲实现协议语义。
/// </summary>
public sealed class SerialPortChannel : ISerialChannel
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen ?? false;

    public void Open(string portName, int baudRate)
    {
        Close();
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1000,
            WriteTimeout = 2000,
        };
        try
        {
            port.Open();
        }
        catch
        {
            // 审计 MC-04：打开失败释放新建实例，且 _port 不保留失败状态（保持 null）
            port.Dispose();
            throw;
        }

        _port = port;
    }

    public void Write(string text)
    {
        if (_port is null || !_port.IsOpen)
        {
            throw new InvalidOperationException("串口未打开");
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        _port.Write(bytes, 0, bytes.Length);
        _port.BaseStream.Flush();   // 与协议规格 §3.1 "Write + Flush" 对齐
    }

    public string ReadAvailable()
    {
        if (_port is null || !_port.IsOpen)
        {
            return "";
        }

        var n = _port.BytesToRead;
        if (n <= 0)
        {
            return "";
        }

        var buf = new byte[n];
        var read = _port.Read(buf, 0, n);
        // 默认替换策略：非法/残缺字节以 U+FFFD 表示（协议为 ASCII，无实际影响）
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    public void ResetInputBuffer() => _port?.DiscardInBuffer();

    public void Close()
    {
        if (_port is not null)
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
            }
            finally
            {
                _port.Dispose();
                _port = null;
            }
        }
    }

    public void Dispose() => Close();
}

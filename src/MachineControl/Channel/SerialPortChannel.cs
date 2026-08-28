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
    private readonly Utf8StreamDecoder _decoder = new();   // 审计 MC-05b：跨批次 UTF-8 解码状态

    /// <summary>当前底层串口实例（internal：供测试验证资源管理，审计复审替代反射）</summary>
    internal SerialPort? Port => _port;

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
        _decoder.Reset();   // 审计复审：新会话清解码状态，防旧会话半截字节跨连接残留
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
        int read;
        try
        {
            read = _port.Read(buf, 0, n);
        }
        catch (TimeoutException)
        {
            // 审计 MC-05a：BytesToRead 快照与实际 Read 之间存在竞态，Read 可能阻塞
            // 至超时（ReadTimeout=1000）。超时 = 当前无数据可读，返回空串让上层按
            // 正常轮询等待（ACK 窗口耗尽判失败），不得抛异常被当作机台错误触发重试
            return "";
        }

        // 审计 MC-05b：跨批次解码（多字节字符拆批不再产生 U+FFFD）
        return _decoder.Append(buf, read);
    }

    public void ResetInputBuffer()
    {
        _port?.DiscardInBuffer();
        _decoder.Reset();   // 审计复审：清接收缓冲须同步清解码状态（与 Mock 残留清除语义对齐）
    }

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

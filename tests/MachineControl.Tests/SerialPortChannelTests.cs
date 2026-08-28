using System.Reflection;
using MachineControl.Channel;
using Xunit;

namespace MachineControl.Tests;

/// <summary>
/// SerialPortChannel（真实串口通道）资源管理行为测试。
/// 不依赖真实串口设备：统一用不存在的端口名触发 Open 失败。
/// </summary>
public class SerialPortChannelTests
{
    private const string NonexistentPort = "NOT_A_REAL_PORT";

    [Fact]
    public void Open_NonexistentPort_ThrowsAndStaysClosed()
    {
        var channel = new SerialPortChannel();

        Assert.ThrowsAny<Exception>(() => channel.Open(NonexistentPort, 9600));
        Assert.False(channel.IsOpen);
    }

    [Fact]
    public void Open_Failure_DoesNotKeepFailedInstance()
    {
        var channel = new SerialPortChannel();

        Assert.ThrowsAny<Exception>(() => channel.Open(NonexistentPort, 9600));

        // 审计 MC-04：打开失败路径不得保留未释放的新建 SerialPort 实例
        var field = typeof(SerialPortChannel).GetField("_port", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Null(field!.GetValue(channel));
    }

    [Fact]
    public void Open_Failure_ThenClose_DoesNotThrow()
    {
        var channel = new SerialPortChannel();

        Assert.ThrowsAny<Exception>(() => channel.Open(NonexistentPort, 9600));
        channel.Close();   // 失败后释放/关闭幂等，不抛异常
        channel.Dispose();
    }
}

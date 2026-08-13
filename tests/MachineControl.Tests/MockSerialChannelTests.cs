using MachineControl.Channel;
using Xunit;

namespace MachineControl.Tests;

/// <summary>MockSerialChannel 自身行为测试（供集成测试引用前先自证可靠）</summary>
public class MockSerialChannelTests
{
    [Fact]
    public void Open_RecordsPortAndBaudRate()
    {
        var mock = new MockSerialChannel();

        mock.Open("COM7", 9600);

        Assert.Equal("COM7", mock.LastOpenedPort);
        Assert.Equal(9600, mock.LastBaudRate);
    }

    [Fact]
    public void OpenError_ThrowsAndDoesNotOpen()
    {
        var mock = new MockSerialChannel { OpenError = "拒绝访问" };

        Assert.Throws<System.IO.IOException>(() => mock.Open("COM4", 9600));
        Assert.False(mock.IsOpen);
        Assert.Null(mock.LastOpenedPort);   // 打开失败不记录
    }
}

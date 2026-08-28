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

    // ---- 审计 MC-03：ResetInputBuffer 对齐真实 DiscardInBuffer（丢弃已到达未读数据） ----

    [Fact]
    public void ResetInputBuffer_DiscardsPreloadedResidual()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.PreloadResidual("残留\r\n");

        mock.ResetInputBuffer();

        Assert.Equal("", mock.ReadAvailable());   // 残留被丢弃（真实 DiscardInBuffer 语义）
    }

    [Fact]
    public void ResetInputBuffer_KeepsFutureResponses()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.EnqueueResponse("ok\r\n");

        mock.ResetInputBuffer();

        Assert.Equal("ok\r\n", mock.ReadAvailable());   // 未来应答不受影响
    }

    [Fact]
    public void ReadAvailable_ReturnsResidualBeforeResponses()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.PreloadResidual("旧数据");
        mock.EnqueueResponse("新应答");

        Assert.Equal("旧数据", mock.ReadAvailable());   // 同一接收缓冲区：先到先读
        Assert.Equal("新应答", mock.ReadAvailable());
    }

    // ---- 审计 MC-06：延迟响应机制（模拟机台回复分片/迟到时序） ----

    [Fact]
    public void EnqueueDelayedResponse_NotReadableBeforeDue()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.EnqueueDelayedResponse("ok\r\n", TimeSpan.FromMilliseconds(300));

        Assert.Equal("", mock.ReadAvailable());   // 未到期：不可读
    }

    [Fact]
    public void EnqueueDelayedResponse_ReadableAfterDue()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.EnqueueDelayedResponse("ok\r\n", TimeSpan.FromMilliseconds(100));

        Thread.Sleep(250);   // 等待到期（短延迟，测试成本可控）
        Assert.Equal("ok\r\n", mock.ReadAvailable());
    }

    // ---- 审计复审：ResetInputBuffer 丢弃已到期未读的延迟响应 ----

    [Fact]
    public void ResetInputBuffer_DiscardsDueDelayedResponses()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.EnqueueDelayedResponse("ok\r\n", TimeSpan.FromMilliseconds(50));

        Thread.Sleep(150);   // 已到期但未读
        mock.ResetInputBuffer();

        Assert.Equal("", mock.ReadAvailable());   // 到期未读项随重置丢弃
    }

    [Fact]
    public void ResetInputBuffer_KeepsPendingDelayedResponses()
    {
        var mock = new MockSerialChannel();
        mock.Open("COM4", 9600);   // 审计复审：Mock 对齐真实通道 IsOpen 语义（Read/Write 未打开不可用）
        mock.EnqueueDelayedResponse("ok\r\n", TimeSpan.FromMilliseconds(200));

        mock.ResetInputBuffer();   // 未到期：尚未到达，不受影响

        Thread.Sleep(300);   // 等待到期
        Assert.Equal("ok\r\n", mock.ReadAvailable());
    }

    // ---- 审计复审：IsOpen 守卫语义（与真实 SerialPortChannel 对齐） ----

    [Fact]
    public void Write_NotOpen_Throws()
    {
        var mock = new MockSerialChannel();   // 未 Open

        Assert.Throws<InvalidOperationException>(() => mock.Write("AT+IO=00\r\n"));
    }

    [Fact]
    public void ReadAvailable_NotOpen_ReturnsEmpty()
    {
        var mock = new MockSerialChannel();   // 未 Open

        Assert.Equal("", mock.ReadAvailable());
    }
}


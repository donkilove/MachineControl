using MachineControl;
using Xunit;

namespace MachineControl.Tests;

/// <summary>机台控制协议测试（对照 BurnMachineHost docs/串口协议规格.md §3）</summary>
public class MachineProtocolTests
{
    [Fact]
    public void AreaASequence_ShouldMatchSpec()
        => Assert.Equal(new[] { "AT+IO=00", "AT+IO=01" }, MachineProtocol.GetSequence(true));

    [Fact]
    public void AreaBSequence_ShouldMatchSpec()
        => Assert.Equal(new[] { "AT+IO=00", "AT+IO=08" }, MachineProtocol.GetSequence(false));

    [Fact]
    public void BuildLine_AppendsCrLf()
        => Assert.Equal("AT+IO=00\r\n", MachineProtocol.BuildLine("AT+IO=00"));

    [Theory]
    [InlineData("ok")]
    [InlineData("OK")]
    [InlineData("Ok")]
    [InlineData("ok\r\n")]
    [InlineData("  ok  ")]
    public void IsAck_VariousOkSpellings_True(string reply)
        => Assert.True(MachineProtocol.IsAck(reply));

    [Theory]
    [InlineData("error")]
    [InlineData("")]
    [InlineData("no")]
    [InlineData(null)]
    public void IsAck_NonOk_False(string? reply)
        => Assert.False(MachineProtocol.IsAck(reply));
}

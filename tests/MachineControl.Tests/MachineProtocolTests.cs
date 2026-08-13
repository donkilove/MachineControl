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

    // ---- 审核修复：序列暴露为只读视图，杜绝 AreaASequence[0] = "x" 全局篡改 ----

    [Fact]
    public void Sequences_ExposeReadOnlyViews()
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        Assert.Equal(typeof(IReadOnlyList<string>),
            typeof(MachineProtocol).GetField("AreaASequence", flags)!.FieldType);
        Assert.Equal(typeof(IReadOnlyList<string>),
            typeof(MachineProtocol).GetField("AreaBSequence", flags)!.FieldType);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(MachineProtocol.GetSequence(true));
        Assert.IsAssignableFrom<IReadOnlyList<string>>(MachineProtocol.GetSequence(false));
    }
}

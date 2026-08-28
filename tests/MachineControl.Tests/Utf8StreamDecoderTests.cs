using System.Text;
using MachineControl.Channel;
using Xunit;

namespace MachineControl.Tests;

/// <summary>
/// Utf8StreamDecoder 跨批次解码行为（审计 MC-05b）：
/// 串口字节流分批到达，多字节字符跨批次时必须正确解码，不得产生 U+FFFD 乱码。
/// </summary>
public class Utf8StreamDecoderTests
{
    [Fact]
    public void Append_AsciiBytes_ReturnsText()
    {
        var decoder = new Utf8StreamDecoder();
        var bytes = Encoding.UTF8.GetBytes("AT+IO=00");

        var result = decoder.Append(bytes, bytes.Length);

        Assert.Equal("AT+IO=00", result);
    }

    [Fact]
    public void Append_MultiByteCharSplitAcrossBatches_DecodesWithoutReplacement()
    {
        var decoder = new Utf8StreamDecoder();
        var bytes = Encoding.UTF8.GetBytes("测");   // UTF-8 三字节序列

        var first = decoder.Append(bytes, 1);       // 仅第 1 字节到达：不完整序列
        var rest = decoder.Append(bytes[1..], 2);   // 后 2 字节到达：凑齐完整字符

        Assert.Equal("", first);                    // 半截字节不得输出乱码 U+FFFD
        Assert.Equal("测", rest);
    }

    [Fact]
    public void Append_InvalidByte_UsesReplacementChar()
    {
        var decoder = new Utf8StreamDecoder();
        byte[] invalid = [0xFF];

        var result = decoder.Append(invalid, 1);

        Assert.Equal("\uFFFD", result);   // 非法字节替换（与原 GetString 默认策略一致）
    }

    [Fact]
    public void Append_MultipleBatches_AccumulatesCorrectly()
    {
        var decoder = new Utf8StreamDecoder();
        var bytes = Encoding.UTF8.GetBytes("AB测C");

        var a = decoder.Append(bytes, 1);          // "A"
        var b = decoder.Append(bytes[1..2], 1);    // "B"
        var c = decoder.Append(bytes[2..4], 2);    // "测" 的前 2 字节（不完整）
        var d = decoder.Append(bytes[4..], 2);     // "测" 末字节 + "C"

        Assert.Equal("A", a);
        Assert.Equal("B", b);
        Assert.Equal("", c);          // 不完整序列不输出
        Assert.Equal("测C", d);
    }
}

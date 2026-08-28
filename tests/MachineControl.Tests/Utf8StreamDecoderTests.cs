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

    // ---- 审计复审：输出缓冲溢出防护（1 字节输入可能产出 2 个 UTF-16 字符） ----

    [Fact]
    public void Append_SurrogatePairSplit_NoOverflow()
    {
        var decoder = new Utf8StreamDecoder();
        var bytes = Encoding.UTF8.GetBytes("😀");   // U+1F600，UTF-8 四字节 F0 9F 98 80

        var first = decoder.Append(bytes, 3);   // 前 3 字节：不完整
        var last = decoder.Append(bytes[3..], 1);   // 最后 1 字节：凑齐 → 代理对 2 个 char

        Assert.Equal("", first);
        Assert.Equal("😀", last);   // 1 字节输入产出 2 个 UTF-16 字符，缓冲不得溢出
    }

    [Fact]
    public void Append_ResidualInterruptedByInvalid_NoOverflow()
    {
        var decoder = new Utf8StreamDecoder();
        byte[] firstPart = [0xE6];   // "测" 首字节（不完整）
        byte[] invalid = [0xFF];     // 非法字节：打断残留序列 → 2 个 U+FFFD

        var first = decoder.Append(firstPart, 1);
        var second = decoder.Append(invalid, 1);

        Assert.Equal("", first);
        Assert.Equal("\uFFFD\uFFFD", second);   // 残留 E6 被替换 + FF 替换 = 2 字符
    }

    // ---- 审计复审：Reset 清解码状态（新会话/清缓冲） ----

    [Fact]
    public void Reset_ClearsPendingDecoderState()
    {
        var decoder = new Utf8StreamDecoder();
        var bytes = Encoding.UTF8.GetBytes("测");

        var half = decoder.Append(bytes, 1);   // 残留 2 字节在 Decoder 状态中
        decoder.Reset();                        // 模拟新会话/清缓冲
        var fresh = decoder.Append(bytes, 3);   // 完整字节应按新序列解码

        Assert.Equal("", half);
        Assert.Equal("测", fresh);   // 不与旧残留拼接成乱码
    }
}

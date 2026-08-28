using System.Text;

namespace MachineControl.Channel;

/// <summary>
/// 带跨批次状态的 UTF-8 流解码器（审计 MC-05b）：
/// 串口字节流按批到达，多字节字符被拆到相邻批次时，解码器记住未完成字节，
/// 凑齐后一次性输出，避免每批独立 GetString 产生 U+FFFD 乱码。
/// </summary>
internal sealed class Utf8StreamDecoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _sb = new();

    /// <summary>追加一批字节，返回本批可完整解出的字符；跨批次残留字节保留在内部状态</summary>
    public string Append(byte[] bytes, int count)
    {
        // 审计 MC-05b：用带状态的 Decoder 替代每批独立 GetString——
        // 多字节字符被拆到相邻批次时，未完成字节保留在 Decoder 状态中，
        // 凑齐后一次性输出，不再产生 U+FFFD 乱码
        _sb.Clear();
        var chars = new char[count];
        var charCount = _decoder.GetChars(bytes, 0, count, chars, 0);
        _sb.Append(chars, 0, charCount);
        return _sb.ToString();
    }
}

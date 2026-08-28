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
        // 审计复审：输出缓冲须大于输入字节数——1 字节输入可能产出 2 个 UTF-16 字符
        // （4 字节字符 3+1 拆分时的代理对；残留序列被非法字节打断时的 2×U+FFFD），
        // 否则 GetChars 抛"output char buffer is too small"
        var chars = new char[count + 4];
        var charCount = _decoder.GetChars(bytes, 0, count, chars, 0);
        _sb.Append(chars, 0, charCount);
        return _sb.ToString();
    }

    /// <summary>重置解码状态（审计复审：新串口会话/清接收缓冲时调用，防旧会话半截字节残留）</summary>
    public void Reset() => _decoder.Reset();
}

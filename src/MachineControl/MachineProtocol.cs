namespace MachineControl;

/// <summary>
/// 机台控制协议：AT+IO 指令序列与 ACK 判定。
/// 规格见 BurnMachineHost 仓库 docs/串口协议规格.md §3，行为与 BurnMachineHost 原版 MachineControlThread 一致。
/// </summary>
public static class MachineProtocol
{
    /// <summary>A 区移动指令序列</summary>
    public static readonly string[] AreaASequence = { "AT+IO=00", "AT+IO=01" };

    /// <summary>B 区移动指令序列</summary>
    public static readonly string[] AreaBSequence = { "AT+IO=00", "AT+IO=08" };

    /// <summary>取指定区域的指令序列</summary>
    public static string[] GetSequence(bool isAreaA)
        => isAreaA ? AreaASequence : AreaBSequence;

    /// <summary>指令行：追加 \r\n</summary>
    public static string BuildLine(string command)
        => command + "\r\n";

    /// <summary>ACK 判定：trim 后小写等于 "ok"（与原版 response.lower() == "ok" 一致）</summary>
    public static bool IsAck(string? response)
        => response?.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase) == true;
}

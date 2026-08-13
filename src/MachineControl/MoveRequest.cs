namespace MachineControl;

/// <summary>机台移动请求（执行器参数）</summary>
/// <param name="MachineSerial">机台控制串口名（如 COM4）</param>
/// <param name="IsAreaA">目标区域：true=A 区，false=B 区</param>
/// <param name="SettleSeconds">到位等待时间（秒）：由调用方按行程类型传入（进区/复位用 move_time_enter，位间用 move_time_between）</param>
public sealed record MoveRequest(string MachineSerial, bool IsAreaA, double SettleSeconds);

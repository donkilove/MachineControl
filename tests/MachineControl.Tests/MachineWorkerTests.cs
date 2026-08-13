using MachineControl;
using MachineControl.Channel;
using Xunit;

namespace MachineControl.Tests;

/// <summary>
/// MachineWorker 集成验证：与协议层、串口通道抽象衔接
/// （对照 BurnMachineHost docs/测试计划.md §5 手工冒烟的可自动化部分）。
/// </summary>
public class MachineWorkerTests
{
    private const string MachineSerial = "COM4";
    private const double MoveTimeEnter = 0.01;
    private const double MoveTimeBetween = 0.01;

    private static MoveRequest NewRequest(bool isAreaA = true, double settleSeconds = MoveTimeEnter)
        => new(MachineSerial, isAreaA, settleSeconds);

    [Fact]
    public async Task MachineWorker_AreaA_AcksBothCommands()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ok\r\n");
        port.EnqueueResponse("OK");
        var statuses = new List<string>();
        var worker = new MachineWorker(() => port, statuses.Add);

        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, port.Writes.Count);
        Assert.Equal("AT+IO=00\r\n", port.Writes[0]);
        Assert.Equal("AT+IO=01\r\n", port.Writes[1]);
        Assert.Contains(statuses, s => s.Contains("机台控制指令执行成功"));
    }

    [Fact]
    public async Task MachineWorker_AreaB_AcksBothCommands()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ok\r\n");
        port.EnqueueResponse("ok\r\n");
        var worker = new MachineWorker(() => port);

        var ok = await worker.MoveToAreaAsync(NewRequest(isAreaA: false, settleSeconds: MoveTimeBetween), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, port.Writes.Count);
        Assert.Equal("AT+IO=00\r\n", port.Writes[0]);
        Assert.Equal("AT+IO=08\r\n", port.Writes[1]);
    }

    [Fact]
    public async Task MachineWorker_NonOkReply_Fails()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ERROR\r\n");   // 每条都失败 → 重试耗尽
        port.EnqueueResponse("ERROR\r\n");
        var worker = new MachineWorker(() => port);

        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(2, port.Writes.Count);   // 两轮重试，每轮都因第一条 ACK 失败而中止
        Assert.All(port.Writes, w => Assert.Contains("AT+IO=00", w));
    }

    // ---- 审核修复：协议健壮性（对照串口协议规格 §3.3） ----

    [Fact]
    public async Task MachineWorker_AckFailure_RetriesAfterOneSecond()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ERROR\r\n");   // 两轮都失败
        port.EnqueueResponse("ERROR\r\n");
        var worker = new MachineWorker(() => port);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);
        sw.Stop();

        Assert.False(ok);
        // 协议规格 §3.3：整轮最多 2 次，失败重试间隔 1 s（审核修复：ACK 失败路径此前无间隔）
        Assert.True(sw.ElapsedMilliseconds >= 900, $"ACK 失败重试应有约 1s 间隔，实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task MachineWorker_OversizeAckReply_Fails()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse(new string('A', 300) + "\r\n");   // 回复长度超限（>256）→ 判失败
        port.EnqueueResponse("ERROR\r\n");
        var worker = new MachineWorker(() => port);

        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public async Task MachineWorker_OpenFailure_RetriesThenFalse()
    {
        var port = new MockSerialChannel { OpenError = "拒绝访问" };
        var worker = new MachineWorker(() => port);

        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);

        Assert.False(ok);
    }

    // ---- 审核补充：重试恢复路径（第一轮失败、第二轮成功） ----

    [Fact]
    public async Task MachineWorker_FirstRoundAckFails_SecondRoundSucceeds()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ERROR\r\n");   // 第一轮：第一条 ACK 失败 → 整轮重试
        port.EnqueueResponse("ok\r\n");      // 第二轮：全部成功
        port.EnqueueResponse("ok");
        var worker = new MachineWorker(() => port);

        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);

        Assert.True(ok);
        // 第一轮：第一条 ACK 失败即中止（写 1 条）；第二轮成功（写 2 条）→ 共 3 条
        Assert.Equal(3, port.Writes.Count);
        Assert.Equal("AT+IO=00\r\n", port.Writes[0]);
        Assert.Equal("AT+IO=00\r\n", port.Writes[1]);
        Assert.Equal("AT+IO=01\r\n", port.Writes[2]);
    }
}

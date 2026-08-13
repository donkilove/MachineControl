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

    // ---- 审核补充：取消契约（OCE 重抛 + 串口关闭，不得误判为失败重试） ----

    [Fact]
    public async Task MachineWorker_CancelDuringAckWait_ThrowsOperationCanceledAndClosesPort()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ok\r\n");   // 第一条 ACK 成功
        // 第二条无回复 → 2s ACK 窗口等待中取消
        var cts = new CancellationTokenSource();
        var worker = new MachineWorker(() => port);

        var task = worker.MoveToAreaAsync(NewRequest(), cts.Token);
        while (port.Writes.Count < 2)
        {
            await Task.Delay(10);
        }

        cts.Cancel();
        // Task.Delay 取消抛 TaskCanceledException（OCE 子类）：语义为取消，非失败重试
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.False(port.IsOpen);   // finally 中已关闭
    }

    [Fact]
    public async Task MachineWorker_CancelDuringRetryDelay_ThrowsOperationCanceledAndClosesPort()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ERROR\r\n");   // 第一轮失败 → 进入 1s 重试间隔
        var cts = new CancellationTokenSource();
        var worker = new MachineWorker(() => port);

        var task = worker.MoveToAreaAsync(NewRequest(), cts.Token);
        while (port.Writes.Count < 1)
        {
            await Task.Delay(10);
        }

        await Task.Delay(100);   // 等待 ACK 失败结算并进入重试间隔
        cts.Cancel();
        // Task.Delay 取消抛 TaskCanceledException（OCE 子类）：语义为取消，非失败重试
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.False(port.IsOpen);
    }

    [Fact]
    public async Task MachineWorker_CancelDuringSettleWait_ThrowsOperationCanceledAndClosesPort()
    {
        var port = new MockSerialChannel();
        port.EnqueueResponse("ok\r\n");
        port.EnqueueResponse("ok");
        var cts = new CancellationTokenSource();
        var worker = new MachineWorker(() => port);

        var task = worker.MoveToAreaAsync(NewRequest(settleSeconds: 30), cts.Token);
        while (port.Writes.Count < 2)
        {
            await Task.Delay(10);
        }

        await Task.Delay(50);   // 进入到位等待
        cts.Cancel();
        // Task.Delay 取消抛 TaskCanceledException（OCE 子类）：语义为取消，非失败重试
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.False(port.IsOpen);
    }

    // ---- 审核补充：ACK 窗口超时（无任何回复）路径 ----

    [Fact]
    public async Task MachineWorker_NoReply_TimesOutAndFails()
    {
        var port = new MockSerialChannel();   // 队列空 → ReadAvailable 恒为空 → 2s 窗口超时
        var statuses = new List<string>();
        var worker = new MachineWorker(() => port, statuses.Add);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = await worker.MoveToAreaAsync(NewRequest(), CancellationToken.None);
        sw.Stop();

        Assert.False(ok);
        // 两轮：每轮 2s 窗口超时 + 1s 重试间隔 ≈ 5s（留 1s 余量）
        Assert.True(sw.ElapsedMilliseconds >= 4000, $"两轮超时+间隔应约 5s，实际 {sw.ElapsedMilliseconds}ms");
        Assert.Contains(statuses, s => s.Contains("超时未收到回复"));
    }

    // ---- 审核修复：入口参数校验（非法输入立即抛参，不得走重试） ----

    [Fact]
    public async Task MoveToAreaAsync_NullRequest_ThrowsArgumentNull()
    {
        var worker = new MachineWorker(() => new MockSerialChannel());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => worker.MoveToAreaAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task MoveToAreaAsync_InvalidSettleSeconds_ThrowsArgumentOutOfRange(double settleSeconds)
    {
        var worker = new MachineWorker(() => new MockSerialChannel());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => worker.MoveToAreaAsync(new MoveRequest(MachineSerial, true, settleSeconds), CancellationToken.None));
    }
}

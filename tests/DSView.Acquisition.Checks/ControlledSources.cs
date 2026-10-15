// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Threading.Channels;
using DSView.Infrastructure;

internal sealed class ScriptSource(AcquisitionPlan plan,
    Func<AcquisitionWriter, CancellationToken, CancellationToken, Task<AcquisitionTermination>> script) : IAcquisitionSource
{
    public AcquisitionPlan Plan => plan;
    public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort) => script(output, stop, abort);
}

// The test advances an actual pending Task.Delay timer, never waits for real time.
internal sealed class ControlledTime : TimeProvider
{
    private readonly Channel<ControlledTimer> pending = Channel.CreateUnbounded<ControlledTimer>();
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public ValueTask<ControlledTimer> NextAsync() => pending.Reader.ReadAsync();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("Only one-shot demo pacing is expected.");
        var timer = new ControlledTimer(callback, state, dueTime, elapsed => Interlocked.Add(ref timestamp, elapsed.Ticks));
        if (!pending.Writer.TryWrite(timer)) throw new InvalidOperationException("Timer queue closed.");
        return timer;
    }
}
internal sealed class ControlledTimer(TimerCallback callback, object? state, TimeSpan due, Action<TimeSpan> advance) : ITimer
{
    private int disposed;
    public TimeSpan Due => due;
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;
    public void Fire(TimeSpan lateness = default)
    {
        if (lateness < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lateness));
        if (Interlocked.Exchange(ref disposed, 1) == 0) { advance(due + lateness); callback(state); }
    }
    public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

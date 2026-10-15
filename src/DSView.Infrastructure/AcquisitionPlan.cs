// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

public enum AcquisitionSourceKind { Demo, Usb }
public enum AcquisitionTermination { Completed, Stopped, Cancelled, Disconnected, Overflow, DataLoss, Failed }
public enum AcquisitionPhase { Preparing, Running, Stopping, Finished }
public sealed record AcquisitionChannel(int Id, string Name);

// A source supplies verified applied settings. A request or USB descriptor alone
// cannot create an authoritative hardware plan.
public sealed class AcquisitionPlan
{
    public AcquisitionPlan(AcquisitionSourceKind kind, string sourceName, long sampleRate,
        long sampleLimit, IEnumerable<AcquisitionChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (!Enum.IsDefined(kind) || !Readable(sourceName, 128) || sampleRate is < 1 or > 1_000_000_000
            || sampleLimit is < 1 or > 64_000_000) throw new ArgumentException("Invalid acquisition source or timing.");
        var copy = channels.Take(33).OrderBy(c => c?.Id).ToArray();
        if (copy.Length is < 1 or > 32 || copy.Any(c => c == null || c.Id is < 0 or > 31 || !Readable(c.Name, 128))
            || copy.Select(c => c.Id).Distinct().Count() != copy.Length) throw new ArgumentException("Invalid acquisition channels.");
        Kind = kind; SourceName = sourceName; SampleRate = sampleRate; SampleLimit = sampleLimit;
        Channels = Array.AsReadOnly(copy);
        ChannelMask = copy.Aggregate(0u, (mask, c) => mask | (1u << c.Id));
    }
    private static bool Readable(string text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum && !text.Any(char.IsControl);
    public AcquisitionSourceKind Kind { get; }
    public string SourceName { get; }
    public long SampleRate { get; }
    public long SampleLimit { get; }
    public IReadOnlyList<AcquisitionChannel> Channels { get; }
    public uint ChannelMask { get; }
}

public sealed record AcquisitionLimits(int BatchSamples = 4096, int QueueBatches = 4, int Transitions = 2_000_000)
{
    internal void Validate()
    {
        if (BatchSamples is < 1 or > 65_536 || QueueBatches is < 1 or > 64 || Transitions is < 1 or > 2_000_000)
            throw new ArgumentOutOfRangeException(nameof(AcquisitionLimits));
    }
}

public interface IAcquisitionSource
{
    AcquisitionPlan Plan { get; }
    // One producer; call Running only after configuration is applied. Copy borrowed
    // buffers with TryWrite before returning from callbacks. False requires stopping.
    // Returning means all source callbacks/transfers have ended, not just data-end.
    Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort);
}

public sealed record AcquisitionProgress(Guid Operation, AcquisitionPhase Phase, long AcceptedSamples, long RetainedSamples);
public sealed record AcquisitionResult(Guid Operation, AcquisitionPlan Plan, AcquisitionTermination Termination,
    long AcceptedSamples, long RetainedSamples, Capture? RawCapture, string Detail)
{
    // The current workbench/file format requires ten samples. Never pad short input.
    public bool IsWorkbenchCompatible => RawCapture is { SampleCount: >= 10 };
    public bool CanPublish => Termination == AcquisitionTermination.Completed && IsWorkbenchCompatible;
    public bool RequiresPartialAcceptance => Termination != AcquisitionTermination.Cancelled && !CanPublish && IsWorkbenchCompatible;
}

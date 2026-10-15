// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using DSView.Domain;

namespace DSView.Infrastructure;

// One consumer owns the transition lists. No mutable list is exposed to rendering.
internal sealed class AcquisitionBuffer(AcquisitionPlan plan, int maximumTransitions)
{
    private readonly List<Edge>[] edges = Enumerable.Range(0, 32).Select(_ => new List<Edge>()).ToArray();
    private uint initial, previous;
    private int transitions;
    public long Count { get; private set; }
    public bool Append(SampleBatch batch)
    {
        if (batch.Start != Count) throw new InvalidDataException("Acquisition consumer lost sample continuity.");
        foreach (uint sample in batch.Samples)
        {
            uint changed = Count == 0 ? 0 : sample ^ previous;
            int added = BitOperations.PopCount(changed);
            if (added > maximumTransitions - transitions) return false;
            if (Count == 0) initial = sample;
            while (changed != 0)
            {
                int channel = BitOperations.TrailingZeroCount(changed);
                edges[channel].Add(new(Count, (sample & (1u << channel)) != 0));
                changed &= changed - 1;
            }
            previous = sample; Count++; transitions += added;
        }
        return true;
    }
    public Capture? Snapshot(AcquisitionTermination end) => Count == 0 ? null : new(plan.SampleRate, Count,
        Array.AsReadOnly(plan.Channels.Select(c => new Signal(c.Id, c.Name, (initial & (1u << c.Id)) != 0,
            Array.AsReadOnly(edges[c.Id].ToArray()))).ToArray()), [])
    {
        DecoderSources = [],
        SourceDescription = $"{plan.Kind}: {plan.SourceName}. {end}; retained {Count} of {plan.SampleLimit} requested samples. " +
            "Raw digital samples. Time zero is the first retained sample."
    };
    public void Release() { for (int i = 0; i < edges.Length; i++) edges[i] = []; }
}

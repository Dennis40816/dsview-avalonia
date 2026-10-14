// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

public sealed record WaveformRun(Edge First, Edge Last, int Count);
public sealed record WaveformProjection(bool Initial, IReadOnlyList<WaveformRun> Runs)
{
    // Rendering only. Picking and measurements always query the original sample coordinates.
    public static WaveformProjection Create(Signal signal, double start, double end, int columns)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || columns < 1)
            throw new ArgumentOutOfRangeException(nameof(start));
        int first = signal.LowerBound((long)Math.Ceiling(start));
        int last = signal.LowerBound((long)Math.Floor(end) + 1);
        bool initial = first > 0 ? signal.Edges[first - 1].High : signal.Initial;
        var runs = new List<WaveformRun>(Math.Min(last - first, columns));
        if (last - first <= columns)
        {
            for (int i = first; i < last; i++) { var edge = signal.Edges[i]; runs.Add(new(edge, edge, 1)); }
        }
        else
        {
            int left = first;
            decimal origin = (decimal)start, span = (decimal)end - origin;
            for (int column = 0; column < columns; column++)
            {
                int right = column == columns - 1 ? last : signal.LowerBound((long)decimal.Ceiling(origin + (column + 1m) * span / columns));
                if (right > left) runs.Add(new(signal.Edges[left], signal.Edges[right - 1], right - left));
                left = right;
            }
        }
        return new(initial, runs);
    }
}

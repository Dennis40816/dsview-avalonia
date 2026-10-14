// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;

namespace DSView.Desktop;

public sealed record MarkerHandle(int Endpoint, Rect Bounds, double SampleX);

public static class MarkerHandles
{
    public static IReadOnlyList<MarkerHandle> Layout(double? a, double? b, double left, double right)
    {
        if (right - left < 48) return [];
        bool Visible(double? x) => x is { } value && value >= left && value <= right;
        a = Visible(a) ? a : null; b = Visible(b) ? b : null;
        if (a is { } ax && b is { } bx && Math.Abs(Math.Clamp(ax - 11, left, right - 22) - Math.Clamp(bx - 11, left, right - 22)) < 26)
        {
            var start = Math.Clamp((ax + bx) / 2 - 24, left, right - 48);
            return [new(0, new(start, 24, 22, 18), ax), new(1, new(start + 26, 24, 22, 18), bx)];
        }
        var result = new List<MarkerHandle>();
        if (a is { } x1) result.Add(new(0, new(Math.Clamp(x1 - 11, left, right - 22), 24, 22, 18), x1));
        if (b is { } x2) result.Add(new(1, new(Math.Clamp(x2 - 11, left, right - 22), 24, 22, 18), x2));
        return result;
    }
}

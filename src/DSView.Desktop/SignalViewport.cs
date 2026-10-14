// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Desktop;

public readonly record struct SignalRow(double Top, double Bottom);
public enum RowVisibility { Visible, Above, Below }

public static class SignalViewport
{
    public static RowVisibility Visibility(SignalRow row, double offset, double height)
    {
        if (height <= 0) return RowVisibility.Visible;
        if (row.Top < offset - .5) return RowVisibility.Above;
        return row.Bottom > offset + height + .5 ? RowVisibility.Below : RowVisibility.Visible;
    }

    public static double RevealOffset(IReadOnlyList<SignalRow> rows, double offset, double height, double extent)
    {
        if (rows.Count == 0 || height <= 0) return offset;
        double top = rows.Min(r => r.Top), bottom = rows.Max(r => r.Bottom);
        // Keep the requested first source visible if both cannot fit. The UI offers
        // a separate recovery action for the other endpoint; Group membership stays intact.
        if (bottom - top > height) { top = rows[0].Top; bottom = rows[0].Bottom; }
        double next = top < offset ? top : bottom > offset + height ? bottom - height : offset;
        return Math.Clamp(next, 0, Math.Max(0, extent - height));
    }
}

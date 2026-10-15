// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Media;

namespace DSView.Desktop;

// Original stroke geometry, independent of platform symbol-font availability.
internal static class ActionIcons
{
    public const string Copy = "M6,6 L14,6 L14,15 L6,15 Z M3,11 L2,11 L2,2 L10,2 L10,3";
    public const string Check = "M2,8 L6,12 L14,4";
    public const string Retry = "M13,6 A5,5 0 1 0 13,11 M13,2 L13,6 L9,6";

    public static Control Create(string path) => new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(path), Stroke = TimelineControl.Ink("#C7D2DB"),
        StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round,
        Width = 18, Height = 18, Stretch = Stretch.Uniform
    };
}

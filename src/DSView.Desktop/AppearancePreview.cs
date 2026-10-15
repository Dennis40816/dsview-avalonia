// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DSView.Infrastructure;

namespace DSView.Desktop;

internal sealed class AppearancePreview : Control
{
    public static readonly StyledProperty<DisplayPreferences> PreferencesProperty = AvaloniaProperty.Register<AppearancePreview, DisplayPreferences>(nameof(Preferences), DisplayPreferences.Default);
    public DisplayPreferences Preferences { get => GetValue(PreferencesProperty); set => SetValue(PreferencesProperty, value); }
    static AppearancePreview() => AffectsRender<AppearancePreview>(PreferencesProperty);
    public AppearancePreview() { Height = 170; MinWidth = 240; }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(TimelineControl.Ink("#101820"), new Rect(Bounds.Size));
        void Text(string text, double x, double y, double size, bool data = false) => context.DrawText(
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(data ? "Cascadia Mono,Consolas" : "fonts:Inter#Inter"), size,
                TimelineControl.Ink("#DCE6ED")), new Point(x, y));
        Text("Waveform preview", 18, 16, Preferences.TextSize);
        Text("D0  SCL", 18, 63, Preferences.TextSize);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new(100, 88), false);
            for (int i = 0; i < 6; i++)
            {
                double x = 100 + i * Math.Max(12, (Bounds.Width - 120) / 6);
                g.LineTo(new(x, 88)); g.LineTo(new(x, 65));
                g.LineTo(new(x + 9, 65)); g.LineTo(new(x + 9, 88));
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(TimelineControl.Ink("#D4AD00"), Preferences.WaveformThickness), geometry);
        Text("0x2A  W    A5 11 02", 18, 124, Preferences.DataTextSize, true);
    }
}

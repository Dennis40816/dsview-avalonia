// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Automation;
using System.Windows.Input;
using Avalonia.Media;

namespace DSView.Desktop;

// Original stroke geometry, independent of platform symbol-font availability.
internal static class ActionIcons
{
    public const string Copy = "M6,6 L14,6 L14,15 L6,15 Z M3,11 L2,11 L2,2 L10,2 L10,3";
    public const string Check = "M2,8 L6,12 L14,4";
    public const string Retry = "M13,6 A5,5 0 1 0 13,11 M13,2 L13,6 L9,6";
    public const string Close = "M3,3 L13,13 M13,3 L3,13";
    public const string Back = "M10,3 L5,8 L10,13 M5,8 L15,8";
    public const string Fit = "M2,6 L2,2 L6,2 M10,2 L14,2 L14,6 M14,10 L14,14 L10,14 M6,14 L2,14 L2,10";
    public const string Settings = "M6,2 L10,2 L10,4 L12,5 L14,4 L16,7 L14,8 L14,10 L16,11 L14,14 L12,13 L10,14 L10,16 L6,16 L6,14 L4,13 L2,14 L0,11 L2,10 L2,8 L0,7 L2,4 L4,5 L6,4 Z M11,9 A3,3 0 1 0 5,9 A3,3 0 1 0 11,9";
    public const string Groups = "M2,2 L6,2 L6,6 L2,6 Z M9,3 L15,3 M9,5 L13,5 M2,10 L6,10 L6,14 L2,14 Z M9,11 L15,11 M9,13 L13,13";
    public const string Add = "M8,2 L8,14 M2,8 L14,8";
    public const string Edit = "M3,11 L11,3 L14,6 L6,14 L2,15 Z M9,5 L12,8";
    public const string Edges = "M1,12 L6,12 L6,3 L11,3 L11,12 L15,12";
    public const string Details = "M2,2 L14,2 L14,14 L2,14 Z M9,2 L9,14 M11,5 L12,5 M11,8 L12,8";

    public static Button Button(string label, string path, ICommand command)
    {
        var button = new Button { Content = Create(path), Command = command, Width = 34, Height = 34, Padding = new(8), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        button.Classes.Add("flat"); ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label); return button;
    }
    public static Control Create(string path) => new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(path), Stroke = TimelineControl.Ink("#C7D2DB"),
        StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round,
        Width = 18, Height = 18, Stretch = Stretch.Uniform
    };
}

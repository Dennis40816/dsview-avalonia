// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using DSView.Infrastructure;

namespace DSView.Desktop;

// UI thread only; projects display preferences into Avalonia resources.
internal static class AppearanceResources
{
    public static void Apply(DisplayPreferences value)
    {
        if (Application.Current is not { } app) return;
        app.Resources["BodyFontSize"] = value.TextSize;
        app.Resources["HeadingFontSize"] = value.TextSize + 2;
        app.Resources["CaptionFontSize"] = value.TextSize - 1;
        app.Resources["DataFontSize"] = value.DataTextSize;
        app.Resources["DataLargeFontSize"] = value.DataTextSize + 1;
    }
}

// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DSView.Desktop;

internal sealed partial class SerialWordsView : UserControl
{
    public SerialWordsView() => AvaloniaXamlLoader.Load(this);
    public Button? ContentFocus => this.FindControl<Button>("CopyData");
    private async void CopyWords(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button || DataContext is not SerialWordsViewModel model || !model.IsCurrent) return;
        string feedback;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) feedback = "Clipboard unavailable";
            else { await clipboard.SetTextAsync(model.CopyText); await clipboard.FlushAsync(); feedback = "Copied"; }
        }
        catch { feedback = "Copy failed · click to retry"; }
        ToolTip.SetTip(button, feedback); AutomationProperties.SetName(button, "Copy data words · " + feedback);
    }
}

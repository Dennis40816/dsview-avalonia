// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace DSView.Desktop;
public partial class App : Avalonia.Application
{
    public override void Initialize() { AvaloniaXamlLoader.Load(this); AppearanceResources.Apply(DSView.Infrastructure.DisplayPreferences.Default); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

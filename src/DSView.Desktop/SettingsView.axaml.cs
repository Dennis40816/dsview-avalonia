// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DSView.Infrastructure;

namespace DSView.Desktop;

internal sealed partial class SettingsView : UserControl
{
    public AppearanceSettingsViewModel Model => (AppearanceSettingsViewModel)DataContext!;
    public SettingsView() => AvaloniaXamlLoader.Load(this);
    public SettingsView(IAppearanceStore store) : this() => DataContext = new AppearanceSettingsViewModel(store);
}

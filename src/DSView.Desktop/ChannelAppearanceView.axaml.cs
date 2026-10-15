// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DSView.Desktop;

internal sealed partial class ChannelAppearanceView : UserControl
{
    public ChannelAppearanceView() => AvaloniaXamlLoader.Load(this);
}

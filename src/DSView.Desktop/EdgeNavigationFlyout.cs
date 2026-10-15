// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;

namespace DSView.Desktop;

internal sealed class EdgeNavigationFlyout : Flyout
{
    public Button Toggle { get; }
    public EdgeNavigationFlyout(Control channel, Control previous, Control next, Action<Flyout> activate)
    {
        Toggle = ActionIcons.Button("Find an edge", ActionIcons.Edges, new RelayCommand(() => { activate(this); ShowAt(Toggle!); }));
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "Find an edge" });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(channel); row.Children.Add(previous); row.Children.Add(next);
        body.Children.Add(row); Content = body; FlyoutPresenterClasses.Add("analysis");
    }
}

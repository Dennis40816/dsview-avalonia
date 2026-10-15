// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
namespace DSView.Desktop;
internal sealed partial class AcquisitionToolbar : UserControl
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public AcquisitionToolbar()
    {
        AvaloniaXamlLoader.Load(this);
        timer.Tick += (_, _) => { if (DataContext is AcquisitionViewModel model && model.IsBusy) model.Poll(); };
        AttachedToVisualTree += (_, _) => timer.Start();
        DetachedFromVisualTree += (_, _) => timer.Stop();
    }
}

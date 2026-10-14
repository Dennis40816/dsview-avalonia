// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

public static class ActivityText
{
    public static string Short(SignalActivity activity) => !activity.Complete ? "Incomplete" : activity.Edges > 0 ? $"{activity.Edges} edges" : activity.ConstantLevel is bool high ? $"{(high ? "High" : "Low")} · 0 edges" : "Level unknown";
    public static string Full(SignalActivity activity) => !activity.Complete ? $"Incomplete · {activity.Edges} observed edges" : activity.Edges > 0 ? $"{activity.Edges} edges" : activity.ConstantLevel is bool high ? $"No edges · {(high ? "High" : "Low")}" : "No edges · level unknown";
}

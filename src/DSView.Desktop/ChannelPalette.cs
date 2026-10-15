// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Desktop;

internal static class ChannelPalette
{
    private static readonly string[] Probes = ["#505050", "#8F5202", "#CC0000", "#F57900", "#EDD400", "#73D216", "#3465A4", "#75507B"];
    public static string Probe(int channel) => Probes[channel % Probes.Length];
    public static string DefaultWaveform(int channel) => channel == 0 ? "#D4AD00" : "#C7D2DB";
}

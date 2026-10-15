// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Media;
using DSView.Domain;

namespace DSView.Desktop;

internal sealed record ProtocolMarkerStyle(IBrush Foreground, IBrush Background)
{
    private static readonly ProtocolMarkerStyle Start = Create("#7DE0A0", "#193B2D");
    private static readonly ProtocolMarkerStyle Stop = Create("#8EC9FF", "#15304A");
    private static readonly ProtocolMarkerStyle Ack = Create("#A3D9B6", "#244234");
    // A NACK is a bus response, including normal read termination, not a diagnostic.
    private static readonly ProtocolMarkerStyle Nack = Create("#F0BD69", "#47351F");
    private static readonly ProtocolMarkerStyle Unknown = Create("#E4EAF0", "#303B46");

    private static ProtocolMarkerStyle Create(string foreground, string background) =>
        new(Brush.Parse(foreground), Brush.Parse(background));

    public static ProtocolMarkerStyle? For(ProtocolItem item) => (item.Packet.Decoder, item.Kind, item.Label) switch
    {
        ("i2c", "point", "START" or "RESTART" or "START REPEAT") => Start,
        ("i2c", "point", "STOP") => Stop,
        ("i2c", "ack", "ACK") => Ack,
        ("i2c", "ack", "NACK") => Nack,
        ("i2c", "ack", _) => Unknown,
        _ => null
    };
}

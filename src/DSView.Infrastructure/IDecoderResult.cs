// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

public interface IDecoderResult
{
    Capture Source { get; }
    DecoderSource Binding { get; }
    IReadOnlyList<Packet> Packets { get; }
    Capture ToCapture();
}

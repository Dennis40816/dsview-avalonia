// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record I2cChannelMap(int Scl, int Sda)
{
    internal Signal[] Resolve(Capture source)
    {
        if (Scl == Sda || Scl is < 0 or > 31 || Sda is < 0 or > 31)
            throw new InvalidDataException("Select two distinct captured channels for SCL and SDA.");
        return new[] { Scl, Sda }.Select(id =>
        {
            var matches = source.Signals.Where(s => s.Id == id).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"I²C channel D{id} is absent or duplicated in this capture.");
            return matches[0];
        }).ToArray();
    }
}

// Keeps physical roles and source identity with the result. This is not a saved
// analysis: mapped results need capture-scoped tracks before UI publication.
public sealed record I2cDecodeResult(Capture Source, I2cChannelMap Channels, IReadOnlyList<Packet> Packets);

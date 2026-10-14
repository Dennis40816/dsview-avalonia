// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

public enum PacketEmptyReason { None, NoCapture, NoRecords, Group, Search }
public sealed record PacketView(IReadOnlyList<Packet> Items, PacketEmptyReason EmptyReason);

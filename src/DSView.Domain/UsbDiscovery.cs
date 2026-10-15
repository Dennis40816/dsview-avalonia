// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

// Descriptor observations only. These records neither grant a device lease nor
// establish driver access, firmware state, acquisition capability or readiness.
public sealed record UsbDeviceObservation(int VendorId, int ProductId, int Revision,
    int Bus, int Address, int Speed, IReadOnlyList<int>? Ports);
public sealed record UsbDiscoverySnapshot(Guid RequestId, DateTimeOffset ObservedAt,
    bool Complete, int UnreadableDescriptors, IReadOnlyList<UsbDeviceObservation> Devices)
{
    // USB addresses and port paths can be reused. No persistent device identity
    // is inferred, even when a later snapshot contains identical descriptors.
    public bool NoMatchingDescriptors => Complete && Devices.Count == 0;
}

// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using DSView.Domain;

namespace DSView.Infrastructure;

internal static class UsbDiscoveryEnvelope
{
    private sealed record Device(int Vendor, int Product, int Revision, int Bus, int Address, int Speed, int[]? Ports);
    private sealed record Envelope(int Schema, string Request, string Scope, int Vendor,
        bool Complete, int UnreadableDescriptors, Device[] Devices);
    public static UsbDiscoverySnapshot Read(string json, Guid request, DateTimeOffset observedAt)
    {
        if (request == Guid.Empty || json.Length > 65_536) throw new InvalidDataException("Invalid USB discovery response size or request.");
        var envelope = JsonSerializer.Deserialize<Envelope>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, AllowDuplicateProperties = false,
            RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8
        }) ?? throw new InvalidDataException("Missing USB discovery response.");
        if (envelope.Schema != 1 || !Guid.TryParseExact(envelope.Request, "N", out var id) || id != request
            || envelope.Scope != "usb-descriptors" || envelope.Vendor != 0x2A0E
            || envelope.UnreadableDescriptors is < 0 or > 4096 || envelope.Complete != (envelope.UnreadableDescriptors == 0)
            || envelope.Devices == null || envelope.Devices.Length > 64)
            throw new InvalidDataException("USB discovery response does not match its request or completeness contract.");
        var locations = new HashSet<(int, int)>(); var devices = new List<UsbDeviceObservation>();
        foreach (var device in envelope.Devices)
        {
            if (device == null || device.Vendor != 0x2A0E || device.Product is < 0 or > 65535 || device.Revision is < 0 or > 65535
                || device.Bus is < 0 or > 255 || device.Address is < 0 or > 127 || device.Speed is < 0 or > 6
                || device.Ports is { Length: < 1 or > 7 } || device.Ports?.Any(p => p is < 1 or > 255) == true
                || !locations.Add((device.Bus, device.Address)))
                throw new InvalidDataException("Invalid or duplicate USB descriptor observation.");
            devices.Add(new(device.Vendor, device.Product, device.Revision, device.Bus, device.Address, device.Speed,
                device.Ports == null ? null : Array.AsReadOnly(device.Ports)));
        }
        return new(request, observedAt, envelope.Complete, envelope.UnreadableDescriptors, devices.AsReadOnly());
    }
}

/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later */
#include "selected-usb.h"
#include <string.h>

void dsv_usb_selection_clear(struct dsv_usb_selection *selection) {
    if (!selection) return;
    libusb_device *old = selection->device;
    memset(selection, 0, sizeof(*selection));
    if (old) libusb_unref_device(old);
}
static int valid(const struct dsv_usb_identity *expected) {
    if (!expected || expected->vendor != 0x2A0E || expected->address > 127 ||
        expected->speed < 0 || expected->speed > 6 || expected->port_count < 0 || expected->port_count > 7) return 0;
    for (int i = 0; i < expected->port_count; i++) if (!expected->ports[i]) return 0;
    return 1;
}
static enum dsv_usb_result matches(libusb_device *device, const struct dsv_usb_identity *expected) {
    struct libusb_device_descriptor descriptor;
    if (libusb_get_device_descriptor(device, &descriptor) != 0) return DSV_USB_UNREADABLE;
    if (descriptor.idVendor != expected->vendor || descriptor.idProduct != expected->product ||
        descriptor.bcdDevice != expected->revision || libusb_get_bus_number(device) != expected->bus ||
        libusb_get_device_address(device) != expected->address || libusb_get_device_speed(device) != expected->speed)
        return DSV_USB_CHANGED;
    if (expected->port_count) {
        uint8_t ports[7]; int count = libusb_get_port_numbers(device, ports, 7);
        if (count <= 0 || count > 7) return DSV_USB_UNREADABLE;
        if (count != expected->port_count || memcmp(ports, expected->ports, count)) return DSV_USB_CHANGED;
    }
    return DSV_USB_OK;
}
enum dsv_usb_result dsv_usb_selection_check(const struct dsv_usb_selection *selection) {
    if (!selection || !selection->device || !valid(&selection->identity)) return DSV_USB_ARGUMENT;
    return matches(selection->device, &selection->identity);
}
enum dsv_usb_result dsv_usb_select(libusb_context *context, const struct dsv_usb_identity *expected,
    struct dsv_usb_selection *selection) {
    if (!selection || !valid(expected)) return DSV_USB_ARGUMENT;
    struct dsv_usb_identity identity = *expected;
    dsv_usb_selection_clear(selection);
    libusb_device **list = NULL;
    ssize_t count = libusb_get_device_list(context, &list);
    enum dsv_usb_result result = DSV_USB_NOT_FOUND;
    libusb_device *found = NULL;
    if (count < 0 || count > 4096 || !list) { result = DSV_USB_ENUMERATION_FAILED; goto done; }
    int locations = 0;
    for (ssize_t i = 0; i < count; i++) {
        if (!list[i]) { result = DSV_USB_ENUMERATION_FAILED; goto done; }
        if (libusb_get_bus_number(list[i]) != identity.bus || libusb_get_device_address(list[i]) != identity.address) continue;
        if (++locations > 1) { result = DSV_USB_AMBIGUOUS; goto done; }
        result = matches(list[i], &identity);
        if (result == DSV_USB_OK) found = list[i];
    }
    if (result == DSV_USB_OK && found) {
        selection->device = libusb_ref_device(found);
        selection->identity = identity;
    }
done:
    if (list) libusb_free_device_list(list, 1);
    return result;
}

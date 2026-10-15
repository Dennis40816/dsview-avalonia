/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later */
#ifndef DSV_SELECTED_USB_H
#define DSV_SELECTED_USB_H
#include <libusb.h>
#include <stdint.h>

struct dsv_usb_identity {
    uint16_t vendor, product, revision;
    uint8_t bus, address;
    int speed, port_count; /* Zero means topology was unavailable. */
    uint8_t ports[7];
};
struct dsv_usb_selection {
    libusb_device *device; /* One retained reference, never a persistent identity. */
    struct dsv_usb_identity identity;
};
enum dsv_usb_result {
    DSV_USB_OK, DSV_USB_ARGUMENT, DSV_USB_NOT_FOUND, DSV_USB_CHANGED,
    DSV_USB_UNREADABLE, DSV_USB_AMBIGUOUS, DSV_USB_ENUMERATION_FAILED
};
/* Zero-initialize selection. No open/claim/transfer calls. A new selection
 * replaces the old reference; callers must close its device owner first. */
enum dsv_usb_result dsv_usb_select(libusb_context *context, const struct dsv_usb_identity *expected,
    struct dsv_usb_selection *selection);
enum dsv_usb_result dsv_usb_selection_check(const struct dsv_usb_selection *selection);
void dsv_usb_selection_clear(struct dsv_usb_selection *selection);
#endif

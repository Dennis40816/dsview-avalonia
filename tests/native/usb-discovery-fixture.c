/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Test DLL: deterministic descriptor observations; never accesses USB. */
#include <stdlib.h>
#include <string.h>
#include <libusb.h>

struct libusb_context { int unused; };
struct libusb_device { int index; };
static struct libusb_context context;
static struct libusb_device devices[3] = {{0}, {1}, {2}};
static libusb_device *list[] = { &devices[0], &devices[1], &devices[2], NULL };
static int mode(const char *expected) {
    const char *value = getenv("DSVIEW_USB_FIXTURE");
    return value && strcmp(value, expected) == 0;
}
int LIBUSB_CALL libusb_init(libusb_context **out) { if (mode("init-failure")) return LIBUSB_ERROR_OTHER; *out = &context; return 0; }
void LIBUSB_CALL libusb_exit(libusb_context *ctx) { (void)ctx; }
ssize_t LIBUSB_CALL libusb_get_device_list(libusb_context *ctx, libusb_device ***out) {
    (void)ctx;
    if (mode("list-failure")) { *out = NULL; return LIBUSB_ERROR_IO; }
    *out = list; return mode("empty") ? 0 : 3;
}
void LIBUSB_CALL libusb_free_device_list(libusb_device **items, int unref) { (void)items; (void)unref; }
int LIBUSB_CALL libusb_get_device_descriptor(libusb_device *dev, struct libusb_device_descriptor *out) {
    if (mode("partial") && dev->index == 0) return LIBUSB_ERROR_IO;
    memset(out, 0, sizeof(*out));
    out->idVendor = dev->index == 1 ? 0x1234 : 0x2A0E;
    out->idProduct = dev->index == 0 ? 0x0020 : 0x0034;
    out->bcdDevice = 0x0201; return 0;
}
uint8_t LIBUSB_CALL libusb_get_bus_number(libusb_device *dev) { (void)dev; return 2; }
uint8_t LIBUSB_CALL libusb_get_device_address(libusb_device *dev) { return (uint8_t)(dev->index + 5); }
int LIBUSB_CALL libusb_get_device_speed(libusb_device *dev) { (void)dev; return LIBUSB_SPEED_HIGH; }
int LIBUSB_CALL libusb_get_port_numbers(libusb_device *dev, uint8_t *ports, int length) {
    (void)length;
    if (mode("unknown-ports")) return LIBUSB_ERROR_NOT_SUPPORTED;
    ports[0] = 3; ports[1] = (uint8_t)(dev->index + 1); return 2;
}

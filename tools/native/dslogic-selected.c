/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Compile in the SAME translation unit after pinned upstream dslogic.c. This
 * narrow adapter reuses its internal profile table/factory/probe setup without
 * copying or calling the unfiltered scan. The build verifies the source pin. */
#include "dslogic-selected.h"

static enum dsv_dslogic_result check_profile(libusb_device *device,
    const struct libusb_device_descriptor *descriptor, int *native_result) {
    libusb_device_handle *handle = NULL;
    int result = libusb_open(device, &handle);
    if (result != 0) { *native_result = result; return DSV_DSLOGIC_ACCESS_FAILED; }
    enum dsv_dslogic_result state = DSV_DSLOGIC_FIRMWARE_REQUIRED;
    if (!descriptor->iManufacturer || !descriptor->iProduct) goto done;
    unsigned char manufacturer[64], product[64];
    int first = libusb_get_string_descriptor_ascii(handle, descriptor->iManufacturer, manufacturer, sizeof(manufacturer));
    if (first < 0) { *native_result = first; state = DSV_DSLOGIC_PROFILE_READ_FAILED; goto done; }
    int second = libusb_get_string_descriptor_ascii(handle, descriptor->iProduct, product, sizeof(product));
    if (second < 0) { *native_result = second; state = DSV_DSLOGIC_PROFILE_READ_FAILED; goto done; }
    static const char maker[] = "DreamSourceLab", model[] = "USB-based DSL Instrument v2";
    if (first == (int)sizeof(maker)-1 && second == (int)sizeof(model)-1 &&
        !memcmp(manufacturer, maker, sizeof(maker)-1) && !memcmp(product, model, sizeof(model)-1)) state = DSV_DSLOGIC_CANDIDATE;
done:
    libusb_close(handle);
    return state;
}

enum dsv_dslogic_result dsv_dslogic_prepare(const struct dsv_usb_selection *selection,
    int allow_firmware, const char *firmware_directory, struct sr_dev_inst **candidate, int *native_result) {
    if (native_result) *native_result = 0;
    if (!selection || !selection->device || !candidate || *candidate || !native_result || (allow_firmware != 0 && allow_firmware != 1)) return DSV_DSLOGIC_ARGUMENT;
    enum dsv_usb_result identity = dsv_usb_selection_check(selection);
    if (identity == DSV_USB_UNREADABLE) return DSV_DSLOGIC_PROFILE_READ_FAILED;
    if (identity != DSV_USB_OK) return DSV_DSLOGIC_IDENTITY_CHANGED;
    struct libusb_device_descriptor descriptor;
    int result = libusb_get_device_descriptor(selection->device, &descriptor);
    if (result != 0) { *native_result = result; return DSV_DSLOGIC_PROFILE_READ_FAILED; }
    int speed = libusb_get_device_speed(selection->device);
    if (descriptor.idVendor != selection->identity.vendor || descriptor.idProduct != selection->identity.product ||
        descriptor.bcdDevice != selection->identity.revision || speed != selection->identity.speed) return DSV_DSLOGIC_IDENTITY_CHANGED;
    const struct DSL_profile *profile = NULL;
    for (int i = 0; supported_DSLogic[i].vid; i++) {
        if (supported_DSLogic[i].vid == descriptor.idVendor && supported_DSLogic[i].pid == descriptor.idProduct &&
            (int)supported_DSLogic[i].usb_speed == speed) { profile = &supported_DSLogic[i]; break; }
    }
    if (!profile) return DSV_DSLOGIC_UNSUPPORTED;
    enum dsv_dslogic_result state = check_profile(selection->device, &descriptor, native_result);
    if (state == DSV_DSLOGIC_FIRMWARE_REQUIRED) {
        if (!allow_firmware) return state;
        if (!firmware_directory || !*firmware_directory) return DSV_DSLOGIC_ARGUMENT;
        char *path = g_build_filename(firmware_directory, profile->firmware, NULL);
        if (!path) return DSV_DSLOGIC_ALLOCATION_FAILED;
        *native_result = ezusb_upload_firmware(selection->device, USB_CONFIGURATION, path);
        g_free(path);
        return *native_result == SR_OK ? DSV_DSLOGIC_REENUMERATION_REQUIRED : DSV_DSLOGIC_FIRMWARE_FAILED;
    }
    if (state != DSV_DSLOGIC_CANDIDATE) return state;
    struct DSL_context *private = DSLogic_dev_new(profile);
    if (!private) return DSV_DSLOGIC_ALLOCATION_FAILED;
    struct sr_dev_inst *device = sr_dev_inst_new(channel_modes[private->ch_mode].mode,
        SR_ST_INITIALIZING, profile->vendor, profile->model, profile->model_version);
    if (!device) { g_free(private); return DSV_DSLOGIC_ALLOCATION_FAILED; }
    device->priv = private; device->driver = &DSLogic_driver_info; device->dev_type = DEV_TYPE_USB;
    device->handle = (ds_device_handle)(uintptr_t)selection->device;
    if (dsl_setup_probes(device, channel_modes[private->ch_mode].num) != SR_OK) { dev_destroy(device); return DSV_DSLOGIC_ALLOCATION_FAILED; }
    struct sr_usb_dev_inst *usb = sr_usb_dev_inst_new(selection->identity.bus, selection->identity.address);
    if (!usb) { dev_destroy(device); return DSV_DSLOGIC_ALLOCATION_FAILED; }
    usb->usb_dev = selection->device; device->conn = usb; device->status = SR_ST_INACTIVE;
    *candidate = device;
    return DSV_DSLOGIC_CANDIDATE;
}

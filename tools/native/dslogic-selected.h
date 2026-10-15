/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later */
#ifndef DSV_DSLOGIC_SELECTED_H
#define DSV_DSLOGIC_SELECTED_H
#include "selected-usb.h"
#include "libsigrok-internal.h"
enum dsv_dslogic_result {
    DSV_DSLOGIC_CANDIDATE, DSV_DSLOGIC_ARGUMENT, DSV_DSLOGIC_IDENTITY_CHANGED,
    DSV_DSLOGIC_UNSUPPORTED, DSV_DSLOGIC_ACCESS_FAILED, DSV_DSLOGIC_PROFILE_READ_FAILED,
    DSV_DSLOGIC_FIRMWARE_REQUIRED, DSV_DSLOGIC_REENUMERATION_REQUIRED,
    DSV_DSLOGIC_FIRMWARE_FAILED, DSV_DSLOGIC_ALLOCATION_FAILED
};
/* Driver must be initialized on the selected context, without lib_main.
 * Keep selection's reference until the candidate/device owner is destroyed.
 * Firmware permission and reviewed resource provenance belong to the caller.
 * CANDIDATE means inactive driver instance, never connected/capture-ready. */
enum dsv_dslogic_result dsv_dslogic_prepare(const struct dsv_usb_selection *selection,
    int allow_firmware, const char *firmware_directory, struct sr_dev_inst **candidate, int *native_result);
#endif

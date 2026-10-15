/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later */
#ifndef DSV_DEVICE_OWNER_H
#define DSV_DEVICE_OWNER_H
#include "libsigrok-internal.h"

/* One native control thread owns this object and the selected driver instance.
 * Do not register the instance with lib_main or allow another thread to destroy
 * or configure it. Acquisition/callback synchronization is a separate boundary.
 * This API establishes connection metadata, never capture readiness. */
enum dsv_connection_result {
    DSV_CONNECTION_OK, DSV_CONNECTION_ARGUMENT, DSV_CONNECTION_OPEN_FAILED,
    DSV_CONNECTION_NOT_ACTIVE, DSV_CONNECTION_READ_FAILED,
    DSV_CONNECTION_STALE, DSV_CONNECTION_GENERATION_EXHAUSTED
};
struct dsv_device_snapshot {
    uint64_t generation, sample_rate, sample_limit;
    int device_type, mode, channel_count;
    char driver[64];
    int channels[32];
};
struct dsv_device_owner {
    struct sr_dev_inst *device;
    uint64_t generation;
};

/* Zero-initialize owner. A valid candidate is consumed (set to NULL) on every
 * open attempt, including failure. Invalid arguments leave ownership unchanged.
 * The previous connection is revoked before opening; there is no fallback.
 * native_result preserves the FIRST dev_open return, unaffected by cleanup. */
enum dsv_connection_result dsv_device_open(struct dsv_device_owner *owner,
    struct sr_dev_inst **candidate, struct dsv_device_snapshot *snapshot, int *native_result);
enum dsv_connection_result dsv_device_read(struct dsv_device_owner *owner,
    uint64_t generation, struct dsv_device_snapshot *snapshot);
/* Revokes before cleanup. dev_destroy return values are not used as evidence:
 * some pinned drivers omit their return. Process exit must still be confirmed. */
void dsv_device_close(struct dsv_device_owner *owner);
#endif

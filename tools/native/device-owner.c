/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later */
#include "device-owner.h"
#include <string.h>

void dsv_device_close(struct dsv_device_owner *owner) {
    if (!owner) return;
    struct sr_dev_inst *old = owner->device;
    owner->device = NULL;
    if (owner->generation != UINT64_MAX) ++owner->generation;
    if (old) old->driver->dev_destroy(old);
}

static int read_u64(const struct sr_dev_inst *device, int key, uint64_t *number) {
    GVariant *value = NULL;
    int result = device->driver->config_get(key, &value, device, NULL, NULL);
    int valid = result == SR_OK && value && g_variant_is_of_type(value, G_VARIANT_TYPE_UINT64);
    if (valid) *number = g_variant_get_uint64(value);
    if (value) g_variant_unref(value);
    return valid;
}

enum dsv_connection_result dsv_device_read(struct dsv_device_owner *owner,
    uint64_t generation, struct dsv_device_snapshot *snapshot) {
    if (snapshot) memset(snapshot, 0, sizeof(*snapshot));
    if (!owner || !snapshot) return DSV_CONNECTION_ARGUMENT;
    if (!generation || generation != owner->generation || !owner->device) return DSV_CONNECTION_STALE;
    const struct sr_dev_inst *device = owner->device;
    if (device->status != SR_ST_ACTIVE) { dsv_device_close(owner); return DSV_CONNECTION_NOT_ACTIVE; }
    struct dsv_device_snapshot value = {0};
    value.generation = generation; value.device_type = device->dev_type; value.mode = device->mode;
    if (device->mode != LOGIC || !device->driver->name || strlen(device->driver->name) >= sizeof(value.driver) ||
        !read_u64(device, SR_CONF_SAMPLERATE, &value.sample_rate) || !value.sample_rate ||
        !read_u64(device, SR_CONF_LIMIT_SAMPLES, &value.sample_limit) || !value.sample_limit) goto invalid;
    strcpy(value.driver, device->driver->name);
    uint32_t seen = 0; int visited = 0;
    for (GSList *l = device->channels; l; l = l->next) {
        const struct sr_channel *ch = l->data;
        if (++visited > 32 || !ch || ch->index >= 32 || ch->type != SR_CHANNEL_LOGIC ||
            (seen & (UINT32_C(1) << ch->index))) goto invalid;
        seen |= UINT32_C(1) << ch->index;
        if (ch->enabled) value.channels[value.channel_count++] = ch->index;
    }
    if (!value.channel_count) goto invalid;
    *snapshot = value;
    return DSV_CONNECTION_OK;
invalid:
    dsv_device_close(owner);
    return DSV_CONNECTION_READ_FAILED;
}

enum dsv_connection_result dsv_device_open(struct dsv_device_owner *owner,
    struct sr_dev_inst **candidate, struct dsv_device_snapshot *snapshot, int *native_result) {
    if (snapshot) memset(snapshot, 0, sizeof(*snapshot));
    if (native_result) *native_result = SR_ERR_ARG;
    if (!owner || !candidate || !*candidate || !snapshot || !native_result) return DSV_CONNECTION_ARGUMENT;
    struct sr_dev_inst *device = *candidate;
    struct sr_dev_driver *driver = device->driver;
    if (device == owner->device || !driver || !driver->dev_open || !driver->dev_destroy || !driver->config_get ||
        device->status != SR_ST_INACTIVE || (device->dev_type != DEV_TYPE_FILELOG && device->dev_type != DEV_TYPE_USB))
        return DSV_CONNECTION_ARGUMENT;
    dsv_device_close(owner);
    *candidate = NULL;
    if (owner->generation == UINT64_MAX) {
        driver->dev_destroy(device); return DSV_CONNECTION_GENERATION_EXHAUSTED;
    }
    int result = driver->dev_open(device);
    *native_result = result;
    if (result != SR_OK) {
        driver->dev_destroy(device); return DSV_CONNECTION_OPEN_FAILED;
    }
    if (device->driver != driver || device->status != SR_ST_ACTIVE) {
        driver->dev_destroy(device); return DSV_CONNECTION_NOT_ACTIVE;
    }
    owner->device = device;
    return dsv_device_read(owner, owner->generation, snapshot);
}

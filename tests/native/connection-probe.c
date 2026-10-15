/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Experimental headless session connection probe. No hardware drivers compiled. */
#include "libsigrok-internal.h"
#include "log.h"
#include <stdio.h>
#include <inttypes.h>
#include <string.h>

/* No generic input/output plug-ins in this headless experiment. The real
 * session driver has its own saved-capture loader. */
struct sr_input_format **sr_input_list(void) {
    static struct sr_input_format *empty[] = {NULL}; return empty;
}
const struct sr_output_module **sr_output_list(void) {
    static const struct sr_output_module *empty[] = {NULL}; return empty;
}
/* The headless build has no hardware option parsers. Fail loudly if a supposedly
 * file-only scenario unexpectedly needs one, rather than supplying fake values. */
int sr_dslogic_option_value_to_code(const struct sr_dev_inst *sdi, int id, const char *value) {
    (void)sdi; (void)id; (void)value; fputs("Unexpected DSLogic hardware option parser.\n", stderr); exit(90);
}
int sr_dscope_option_value_to_code(const struct sr_dev_inst *sdi, int id, const char *value) {
    (void)sdi; (void)id; (void)value; fputs("Unexpected DSCope hardware option parser.\n", stderr); exit(91);
}
struct file_lease {
    ds_device_handle handle;
    int activations;
    uint64_t generation;
};
struct readback {
    uint64_t rate, samples;
    int count, ids[32];
};

static int read_u64(int key, uint64_t *number) {
    GVariant *value = NULL;
    int result = ds_get_actived_device_config(NULL, NULL, key, &value);
    int valid = result == SR_OK && value && g_variant_is_of_type(value, G_VARIANT_TYPE_UINT64);
    if (valid) *number = g_variant_get_uint64(value);
    if (value) g_variant_unref(value);
    return valid;
}

/* Only file devices, one controlling thread and no removal command. Borrowed
 * info.di/channels cannot be destroyed concurrently in this experiment.
 * USB/hotplug MUST use an atomic owned snapshot under native synchronization;
 * these pointer reads are NOT a production hardware connection implementation. */
static const char *read_exact(ds_device_handle expected, int activations,
    struct ds_device_full_info *info, struct readback *state) {
    memset(info, 0, sizeof(*info)); memset(state, 0, sizeof(*state));
    if (ds_get_actived_device_info(info) != SR_OK) return "no-active-info";
    if (info->handle != expected || !expected) return "wrong-device";
    if (info->dev_type != DEV_TYPE_FILELOG || strcmp(info->driver_name, "virtual-session")) return "wrong-driver";
    if (!info->di || info->di->status != SR_ST_ACTIVE) return "not-active";
    if (info->actived_times <= 0 || (activations > 0 && info->actived_times != activations)) return "stale-activation";
    if (ds_get_actived_device_mode() != LOGIC) return "unsupported-mode";
    if (!read_u64(SR_CONF_SAMPLERATE, &state->rate) || !state->rate ||
        !read_u64(SR_CONF_LIMIT_SAMPLES, &state->samples) || !state->samples) return "invalid-settings";
    uint32_t seen = 0;
    int visited = 0;
    for (GSList *l = ds_get_actived_device_channels(); l; l = l->next) {
        const struct sr_channel *ch = l->data;
        if (++visited > 32 || !ch || ch->index >= 32 || ch->type != SR_CHANNEL_LOGIC ||
            (seen & (UINT32_C(1) << ch->index))) return "invalid-channels";
        seen |= UINT32_C(1) << ch->index;
        if (ch->enabled) state->ids[state->count++] = ch->index;
    }
    return state->count ? "verified" : "no-enabled-channels";
}

static void report(const char *event, int api, ds_device_handle expected, uint64_t generation,
    const char *reason, const struct ds_device_full_info *info, const struct readback *state) {
    printf("{\"event\":\"%s\",\"scope\":\"file-session-experiment\",\"api\":%d,"
        "\"expected\":\"%"PRIu64"\",\"actual\":\"%"PRIu64"\",\"generation\":%"PRIu64","
        "\"activations\":%d,\"nativeStatus\":%d,\"reason\":\"%s\",\"connected\":%s,"
        "\"sampleRate\":%"PRIu64",\"sampleCount\":%"PRIu64",\"channels\":[",
        event, api, (uint64_t)expected, (uint64_t)info->handle, generation, info->actived_times,
        info->di ? info->di->status : -1, reason, strcmp(reason, "verified") ? "false" : "true",
        state->rate, state->samples);
    for (int i = 0; i < state->count; i++) printf("%s%d", i ? "," : "", state->ids[i]);
    puts("]}"); fflush(stdout);
}

int main(int argc, char **argv) {
    if (argc < 2 || argc > 3) return 2;
    sr_log_init(); ds_log_level(0);
    if (ds_lib_init() != SR_OK) return 3;
    int result = 4, count = 0;
    struct ds_device_base_info *list = NULL;
    struct file_lease lease = {0}, previous = {0};
    uint64_t generation = 0;
    for (int i = 1; i < argc; i++) if (ds_device_from_file(argv[i]) != SR_OK) goto done;
    if (ds_get_device_list(&list, &count) != SR_OK || count != argc - 1) goto done;
    printf("{\"event\":\"registered\",\"count\":%d}\n", count); fflush(stdout);
    char command[128]; result = 0;
    while (fgets(command, sizeof(command), stdin)) {
        if (!strchr(command, '\n')) { result = 5; break; }
        command[strcspn(command, "\r\n")] = '\0';
        struct ds_device_full_info info = {0};
        struct readback state = {0};
        if (!strcmp(command, "open 0") || !strcmp(command, "open 1")) {
            int slot = command[5] - '0';
            if (slot >= count) { result = 5; break; }
            previous = lease; memset(&lease, 0, sizeof(lease)); ++generation;
            int api = ds_active_device(list[slot].handle);
            const char *reason = read_exact(list[slot].handle, 0, &info, &state);
            if (api != SR_OK) reason = "activation-failed";
            if (!strcmp(reason, "verified")) {
                lease.handle = info.handle; lease.activations = info.actived_times; lease.generation = generation;
            }
            report("open", api, list[slot].handle, generation, reason, &info, &state);
            if (!lease.handle) ds_release_actived_device();
        } else if (!strcmp(command, "read") || !strcmp(command, "read previous")) {
            struct file_lease selected = !strcmp(command, "read") ? lease : previous;
            const char *reason = !selected.handle ? "no-lease" : selected.generation != generation ? "stale-generation" :
                read_exact(selected.handle, selected.activations, &info, &state);
            report("read", SR_OK, selected.handle, selected.generation, reason, &info, &state);
        } else if (!strcmp(command, "release")) {
            ds_device_handle old = lease.handle; int epoch = lease.activations;
            memset(&lease, 0, sizeof(lease));
            int api = ds_release_actived_device();
            const char *reason = read_exact(old, epoch, &info, &state);
            report("release", api, old, generation, reason, &info, &state);
        } else if (!strcmp(command, "close")) break;
        else { result = 5; break; }
    }
done:
    g_free(list);
    int cleanup = ds_lib_exit();
    printf("{\"event\":\"closed\",\"cleanup\":%d,\"result\":%d}\n", cleanup, result); fflush(stdout);
    return cleanup == SR_OK ? result : 6;
}

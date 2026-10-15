/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Real saved-session driver exercised through the product native owner. */
#include "device-owner.h"
#include "log.h"
#include <stdio.h>
#include <inttypes.h>
#include <string.h>

/* Link-time headless support; lib_main is not linked. No hardware manager or
 * hotplug thread is initialized. Unexpected acquisition/configuration calls
 * terminate rather than substituting successful fake behavior. */
struct sr_input_format **sr_input_list(void) { static struct sr_input_format *empty[] = {NULL}; return empty; }
const struct sr_output_module **sr_output_list(void) { static const struct sr_output_module *empty[] = {NULL}; return empty; }
int ds_dsl_option_value_to_code(int mode, int key, const char *value) {
    (void)mode; (void)key; (void)value; exit(90);
}
int ds_data_forward(const struct sr_dev_inst *sdi, const struct sr_datafeed_packet *packet) {
    (void)sdi; (void)packet; exit(91);
}
int current_device_acquisition_stop(void) { exit(92); }

static const char *result_name(enum dsv_connection_result result) {
    static const char *names[] = {"ok", "argument", "open-failed", "not-active", "read-failed", "stale", "generation-exhausted"};
    return names[result];
}
static void emit(const char *event, enum dsv_connection_result result, int native,
    const struct dsv_device_owner *owner, const struct dsv_device_snapshot *state) {
    printf("{\"event\":\"%s\",\"result\":\"%s\",\"native\":%d,\"owned\":%s,"
        "\"generation\":%"PRIu64",\"snapshotGeneration\":%"PRIu64",\"sampleRate\":%"PRIu64","
        "\"sampleLimit\":%"PRIu64",\"driver\":\"%s\",\"channels\":[",
        event, result_name(result), native, owner->device ? "true" : "false", owner->generation,
        state->generation, state->sample_rate, state->sample_limit, state->driver);
    for (int i = 0; i < state->channel_count; i++) printf("%s%d", i ? "," : "", state->channels[i]);
    puts("]}"); fflush(stdout);
}
int main(int argc, char **argv) {
    if (argc < 2 || argc > 3) return 2;
    sr_log_init(); ds_log_level(0);
    struct sr_dev_inst *candidates[2] = {0};
    struct dsv_device_owner owner = {0};
    struct dsv_device_snapshot saved = {0};
    uint64_t previous_generation = 0;
    int exit_code = 0;
    for (int i = 1; i < argc; i++) if (sr_new_virtual_device(argv[i], &candidates[i-1]) != SR_OK) { exit_code = 3; goto done; }
    printf("{\"event\":\"registered\",\"count\":%d}\n", argc-1); fflush(stdout);
    char command[128];
    while (fgets(command, sizeof(command), stdin)) {
        if (!strchr(command, '\n')) { exit_code = 4; break; }
        command[strcspn(command, "\r\n")] = '\0';
        struct dsv_device_snapshot snapshot = {0};
        if (!strcmp(command, "open 0") || !strcmp(command, "open 1")) {
            int slot = command[5] - '0';
            if (slot >= argc-1) { exit_code = 4; break; }
            previous_generation = owner.generation;
            if (!candidates[slot] && sr_new_virtual_device(argv[slot+1], &candidates[slot]) != SR_OK) { exit_code = 3; break; }
            int native = 999;
            enum dsv_connection_result result = dsv_device_open(&owner, &candidates[slot], &snapshot, &native);
            if (result == DSV_CONNECTION_OK) saved = snapshot;
            emit("open", result, native, &owner, &snapshot);
        } else if (!strcmp(command, "read") || !strcmp(command, "read previous")) {
            uint64_t generation = !strcmp(command, "read") ? owner.generation : previous_generation;
            enum dsv_connection_result result = dsv_device_read(&owner, generation, &snapshot);
            emit("read", result, 0, &owner, &snapshot);
        } else if (!strcmp(command, "saved")) {
            emit("saved", DSV_CONNECTION_OK, 0, &owner, &saved);
        } else if (!strcmp(command, "release")) {
            dsv_device_close(&owner);
            emit("release", DSV_CONNECTION_OK, 0, &owner, &snapshot);
        } else if (!strcmp(command, "close")) break;
        else { exit_code = 4; break; }
    }
done:
    dsv_device_close(&owner);
    for (int i = 0; i < 2; i++) if (candidates[i]) candidates[i]->driver->dev_destroy(candidates[i]);
    sr_log_uninit();
    printf("{\"event\":\"closed\",\"result\":%d,\"owned\":false}\n", exit_code); fflush(stdout);
    return exit_code;
}

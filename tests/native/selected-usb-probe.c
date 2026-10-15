/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Controlled USB transport; real DSLogic profile/factory/probe setup. Never USB IO. */
#include "dslogic-selected.h"
#include "device-owner.h"
#include "hardware/DSL/command.h"
#include "log.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

struct libusb_device { int index, refs; };
struct libusb_device_handle { libusb_device *device; };
static struct libusb_device devices[3] = {{0,1},{1,1},{2,1}};
static struct libusb_device_handle handles[3];
static const char *scenario;
static int phase, enumerations, opens[3], firmware[3], controls, claims, last_error;
static int is(const char *name) { return !strcmp(scenario, name); }
char DS_RES_PATH[500] = "C:/fixture-resources";
char DS_USR_PATH[500] = "C:/fixture-resources";
extern struct sr_dev_driver DSLogic_driver_info;

ssize_t LIBUSB_CALL libusb_get_device_list(libusb_context *ctx, libusb_device ***out) {
    (void)ctx; ++enumerations;
    if (is("enumeration-failure")) { *out = NULL; return LIBUSB_ERROR_IO; }
    int count = is("missing") ? 1 : 2;
    *out = calloc((size_t)count+1, sizeof(**out));
    if (!*out) return LIBUSB_ERROR_NO_MEM;
    (*out)[0] = is("missing") ? &devices[1] : &devices[0];
    if (count == 2) (*out)[1] = &devices[1];
    for (int i = 0; i < count; i++) ++(*out)[i]->refs;
    return count;
}
libusb_device * LIBUSB_CALL libusb_ref_device(libusb_device *device) { ++device->refs; return device; }
void LIBUSB_CALL libusb_unref_device(libusb_device *device) { if (--device->refs < 1) exit(94); }
void LIBUSB_CALL libusb_free_device_list(libusb_device **list, int unref) {
    for (int i = 0; list[i]; i++) if (unref) libusb_unref_device(list[i]);
    free(list);
}
uint8_t LIBUSB_CALL libusb_get_bus_number(libusb_device *device) { (void)device; return 200; }
uint8_t LIBUSB_CALL libusb_get_device_address(libusb_device *device) { return device->index == 1 && !is("ambiguous") ? 7 : 5; }
int LIBUSB_CALL libusb_get_device_speed(libusb_device *device) { (void)device; return is("low-speed") ? LIBUSB_SPEED_FULL : LIBUSB_SPEED_HIGH; }
int LIBUSB_CALL libusb_get_device_descriptor(libusb_device *device, struct libusb_device_descriptor *out) {
    if (device->index == 0 && (is("descriptor-failure") || (phase && is("disconnected")))) return LIBUSB_ERROR_NO_DEVICE;
    memset(out, 0, sizeof(*out)); out->idVendor = 0x2A0E;
    out->idProduct = is("unknown-product") ? 0xffff : 0x20;
    out->bcdDevice = phase && is("changed-revision") ? 0x201 : 0x200;
    out->iManufacturer = 1; out->iProduct = 2;
    return 0;
}
int LIBUSB_CALL libusb_get_port_numbers(libusb_device *device, uint8_t *ports, int length) {
    (void)length;
    if (is("unknown-ports")) return LIBUSB_ERROR_NOT_SUPPORTED;
    ports[0] = 3; ports[1] = (uint8_t)(phase && is("changed-port") ? 4 : device->index+1); return 2;
}
int LIBUSB_CALL libusb_open(libusb_device *device, libusb_device_handle **out) {
    ++opens[device->index];
    if (is("access-denied") || (phase == 2 && is("driver-open-denied"))) { *out = NULL; return LIBUSB_ERROR_ACCESS; }
    handles[device->index].device = device; *out = &handles[device->index]; return 0;
}
void LIBUSB_CALL libusb_close(libusb_device_handle *handle) { (void)handle; }
int LIBUSB_CALL libusb_get_string_descriptor_ascii(libusb_device_handle *handle, uint8_t index, unsigned char *out, int length) {
    (void)handle;
    if (is("string-failure")) return LIBUSB_ERROR_IO;
    const char *text = index == 1 ? "DreamSourceLab" : "USB-based DSL Instrument v2";
    if (is("firmware-required") || is("firmware-allowed") || is("firmware-failed")) text = "Bootloader";
    int count = (int)strlen(text);
    if (count >= length) return LIBUSB_ERROR_OVERFLOW;
    memcpy(out, text, count+1); return count;
}
int ezusb_upload_firmware(libusb_device *device, int configuration, const char *path) {
    (void)configuration; ++firmware[device->index];
    if (!strstr(path, "DSLogicPlus.fw")) exit(95);
    return is("firmware-failed") ? SR_ERR : SR_OK;
}
int LIBUSB_CALL libusb_control_transfer(libusb_device_handle *h, uint8_t r, uint8_t q, uint16_t v, uint16_t i, unsigned char *d, uint16_t n, unsigned int t) {
    (void)r;(void)v;(void)i;(void)t;
    if (phase != 2 || h->device != &devices[0]) exit(96);
    ++controls;
    if (q == CMD_CTL_RD_PRE && n == sizeof(struct ctl_header) && ((struct ctl_header *)d)->dest == DSL_CTL_FW_VERSION)
        return is("short-pre") ? n-1 : n;
    if (q == CMD_CTL_RD && n == 2) {
        d[0] = is("incompatible") ? 1 : 2; d[1] = 0;
        return is("short-read") ? 1 : 2;
    }
    exit(96);
}
int LIBUSB_CALL libusb_claim_interface(libusb_device_handle *h, int number) { (void)h;(void)number; ++claims; exit(98); }
int LIBUSB_CALL libusb_release_interface(libusb_device_handle *h, int number) { (void)h;(void)number; return LIBUSB_ERROR_NOT_FOUND; }
struct sr_input_format **sr_input_list(void) { static struct sr_input_format *empty[]={NULL}; return empty; }
const struct sr_output_module **sr_output_list(void) { static const struct sr_output_module *empty[]={NULL}; return empty; }
int sr_usb_device_is_exists(libusb_device *device) { (void)device; exit(97); }
int post_message_callback(int message) { (void)message; exit(97); }
void ds_set_last_error(int error) { last_error=error; }
int ds_dsl_option_value_to_code(int mode, int key, const char *value) { (void)mode;(void)key;(void)value; exit(97); }
int ds_data_forward(const struct sr_dev_inst *d, const struct sr_datafeed_packet *p) { (void)d;(void)p; exit(97); }
int current_device_acquisition_stop(void) { exit(97); }

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    scenario = argv[1];
    sr_log_init(); ds_log_level(0);
    struct sr_context context = {0};
    if (DSLogic_driver_info.init(&context) != SR_OK) return 3;
    struct dsv_usb_identity expected = {.vendor=0x2A0E,.product=0x20,.revision=0x200,.bus=200,.address=5,
        .speed=LIBUSB_SPEED_HIGH,.port_count=2,.ports={3,1}};
    if (is("unknown-product")) expected.product=0xffff;
    if (is("low-speed")) expected.speed=LIBUSB_SPEED_FULL;
    if (is("unknown-ports")) expected.port_count=0;
    struct dsv_usb_selection selection = {0};
    enum dsv_usb_result selected = dsv_usb_select(NULL, &expected, &selection);
    phase=1;
    struct sr_dev_inst *candidate=NULL;
    int prepared=-1, native=0, channels=0, candidate_ok=0, opened=-1, open_native=0;
    struct dsv_device_owner owner={0}; struct dsv_device_snapshot snapshot={0};
    if (selected == DSV_USB_OK) {
        int allow = is("firmware-allowed") || is("firmware-failed") || is("access-denied") || is("string-failure");
        prepared=dsv_dslogic_prepare(&selection, allow, "C:/fixture-resources", &candidate, &native);
    }
    if (candidate) {
        struct sr_usb_dev_inst *usb=candidate->conn;
        channels=g_slist_length(candidate->channels);
        candidate_ok=candidate->status==SR_ST_INACTIVE && candidate->driver==&DSLogic_driver_info &&
            candidate->dev_type==DEV_TYPE_USB && usb && usb->usb_dev==&devices[0] && channels==16;
        if (is("driver-open-denied") || is("incompatible") || is("short-pre") || is("short-read")) {
            phase=2;
            opened=dsv_device_open(&owner,&candidate,&snapshot,&open_native);
        }
        if (candidate) candidate->driver->dev_destroy(candidate);
    }
    int owned=owner.device!=NULL;
    dsv_device_close(&owner);
    dsv_usb_selection_clear(&selection);
    DSLogic_driver_info.cleanup(); sr_log_uninit();
    printf("{\"selected\":%d,\"prepared\":%d,\"native\":%d,\"candidate\":%s,\"channels\":%d,"
        "\"opens\":[%d,%d,%d],\"firmware\":[%d,%d,%d],\"references\":[%d,%d,%d],\"enumerations\":%d,"
        "\"opened\":%d,\"openNative\":%d,\"owned\":%s,\"controls\":%d,\"claims\":%d,\"lastError\":%d}\n",
        selected,prepared,native,candidate_ok?"true":"false",channels,opens[0],opens[1],opens[2],
        firmware[0],firmware[1],firmware[2],devices[0].refs,devices[1].refs,devices[2].refs,enumerations,
        opened,open_native,owned?"true":"false",controls,claims,last_error);
    return 0;
}

/* SPDX-FileCopyrightText: 2026 Dennis40816
 * SPDX-License-Identifier: GPL-3.0-or-later
 * Windows descriptor-only discovery. No device open, interface claims, control
 * transfers, firmware upload, libsigrok initialization or persistent handles. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <libusb.h>

struct observed {
    struct libusb_device_descriptor descriptor;
    unsigned bus, address;
    int speed, port_count;
    uint8_t ports[7];
};

int wmain(int argc, wchar_t **argv) {
    char request[33];
    if (argc != 3 || wcslen(argv[2]) != 32) return 2;
    for (int i = 0; i < 32; i++) {
        wchar_t c = argv[2][i];
        if (!((c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f') || (c >= L'A' && c <= L'F'))) return 2;
        request[i] = (char)c;
    }
    request[32] = '\0';
    HMODULE module = LoadLibraryExW(argv[1], NULL, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) { fprintf(stderr, "Cannot load reviewed USB runtime (%lu).\n", GetLastError()); return 3; }
    /* Resolve only descriptor APIs; load dependencies from the DLL directory
       and Windows system directory, never from the current working directory. */
#define RESOLVE(name) \
    __typeof__(&name) call_##name; \
    do { FARPROC symbol = GetProcAddress(module, #name); \
         if (!symbol) { fprintf(stderr, "Missing USB API: %s.\n", #name); FreeLibrary(module); return 3; } \
         _Static_assert(sizeof(symbol) == sizeof(call_##name), "Function pointer size mismatch"); \
         memcpy(&call_##name, &symbol, sizeof(symbol)); } while (0)
    RESOLVE(libusb_init);
    RESOLVE(libusb_exit);
    RESOLVE(libusb_get_device_list);
    RESOLVE(libusb_free_device_list);
    RESOLVE(libusb_get_device_descriptor);
    RESOLVE(libusb_get_bus_number);
    RESOLVE(libusb_get_device_address);
    RESOLVE(libusb_get_device_speed);
    RESOLVE(libusb_get_port_numbers);
#undef RESOLVE
    libusb_context *context = NULL;
    int result = call_libusb_init(&context);
    if (result != 0) { fprintf(stderr, "USB initialization failed (%d).\n", result); FreeLibrary(module); return 4; }
    libusb_device **list = NULL;
    ssize_t count = call_libusb_get_device_list(context, &list);
    int failed = count < 0 || count > 4096 || list == NULL;
    if (failed) fprintf(stderr, "USB device enumeration failed or exceeded its bound.\n");
    struct observed records[64] = {0}; unsigned found = 0, unreadable = 0;
    for (ssize_t i = 0; !failed && i < count; i++) {
        struct libusb_device_descriptor descriptor;
        if (!list[i]) { failed = 1; break; }
        if (call_libusb_get_device_descriptor(list[i], &descriptor) != 0) { unreadable++; continue; }
        if (descriptor.idVendor != 0x2A0E) continue;
        if (found == 64) { fprintf(stderr, "Too many matching USB devices.\n"); failed = 1; break; }
        struct observed *record = &records[found++];
        record->descriptor = descriptor;
        record->bus = call_libusb_get_bus_number(list[i]);
        record->address = call_libusb_get_device_address(list[i]);
        record->speed = call_libusb_get_device_speed(list[i]);
        record->port_count = call_libusb_get_port_numbers(list[i], record->ports, 7);
        if (record->port_count > 7) { failed = 1; break; }
    }
    if (list) call_libusb_free_device_list(list, 1);
    call_libusb_exit(context); FreeLibrary(module);
    if (failed) return 5;
    printf("{\"schema\":1,\"request\":\"%s\",\"scope\":\"usb-descriptors\",\"vendor\":10766,\"complete\":%s,\"unreadableDescriptors\":%u,\"devices\":[",
        request, unreadable == 0 ? "true" : "false", unreadable);
    for (unsigned i = 0; i < found; i++) {
        const struct observed *record = &records[i];
        if (i) putchar(',');
        printf("{\"vendor\":%u,\"product\":%u,\"revision\":%u,\"bus\":%u,\"address\":%u,\"speed\":%d,\"ports\":",
            record->descriptor.idVendor, record->descriptor.idProduct, record->descriptor.bcdDevice,
            record->bus, record->address, record->speed);
        if (record->port_count <= 0) printf("null");
        else {
            putchar('[');
            for (int j = 0; j < record->port_count; j++) { if (j) putchar(','); printf("%u", record->ports[j]); }
            putchar(']');
        }
        putchar('}');
    }
    printf("]}\n");
    return fflush(stdout) || ferror(stdout) ? 6 : 0;
}

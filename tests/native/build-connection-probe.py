# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Build the file-only connection probe from an exact, unmodified DSView revision.

Requires an existing MinGW-w64 compiler and reviewed GLib/libusb/zlib development
packages. Does not download dependencies or modify the upstream checkout.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess

PIN = '2e9e2c8e726df4ef5687d39b83d4f797cc44b574'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--direct-owner', action='store_true', help='Build the product owner probe instead of the upstream global-manager probe')
for name in ('upstream', 'compiler-root', 'glib-root', 'extra-root', 'output'):
    parser.add_argument('--' + name, type=Path, required=True)
args = parser.parse_args()
upstream = args.upstream.resolve(strict=True)
compiler = args.compiler_root.resolve(strict=True)
glib = args.glib_root.resolve(strict=True)
extra = args.extra_root.resolve(strict=True)
root = args.output.resolve()
root.mkdir(parents=True, exist_ok=False)
vendor = root / 'vendor'
(root / 'bin').mkdir()

units = ['backend', 'hwdriver', 'lib_main', 'trigger', 'session_driver', 'session',
         'dsdevice', 'strutil', 'std', 'log']
if args.direct_owner:
    units.remove('lib_main')
names = [f'libsigrok4DSL/{s}.c' for s in units]
names += ['libsigrok4DSL/' + n for n in ['libsigrok.h', 'libsigrok-internal.h', 'version.h', 'log.h', 'COPYING']]
names += ['common/' + n for n in ['ds_types.h', 'log/xlog.c', 'log/xlog.h',
          'minizip/unzip.c', 'minizip/unzip.h', 'minizip/ioapi.c', 'minizip/ioapi.h', 'minizip/of.h']]
records = []
for name in names:
    data = subprocess.check_output(['git', 'show', PIN + ':' + name], cwd=upstream)
    target = vendor / name
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    records.append({'path': name, 'sha256': hashlib.sha256(data).hexdigest()})

# No HAVE_DSL_DEVICE or HAVE_LA_DEMO: sr_driver_list is genuinely empty.
# Only explicitly registered file sessions exist. Real libusb initialization and
# the original management/hotplug thread still run; no hardware driver can scan.
config = '''#pragma once
#define SR_CONFIG_H
#define HAVE_STDINT_H 1
#define HAVE_INTTYPES_H 1
#define HAVE_STDLIB_H 1
#define HAVE_STRING_H 1
#define HAVE_LIBUSB_1_0 1
#define PACKAGE_BUGREPORT "support@dreamsourcelab.com"
#define PACKAGE_NAME "libsigrok4DSL"
#define PACKAGE_STRING "libsigrok4DSL 0.2.0"
#define PACKAGE_TARNAME "libsigrok4DSL"
#define PACKAGE_URL "http://www.dreamsourcelab.com"
#define PACKAGE_VERSION "0.2.0"
'''
(vendor / 'libsigrok4DSL/config.h').write_bytes(config.encode('utf-8'))
source = Path(__file__).with_name('device-owner-probe.c' if args.direct_owner else 'connection-probe.c').resolve()
command = [str(compiler / 'bin/gcc.exe'), '-std=gnu11', '-O1', '-g', '-Wall', '-Wextra',
           '-ffunction-sections', '-fdata-sections', '-Wl,--gc-sections', '-static-libgcc']
command += ['-I' + str(p) for p in [vendor / 'libsigrok4DSL', vendor / 'common',
            extra / 'include', extra / 'include/libusb-1.0', glib / 'include/glib-2.0', glib / 'lib/glib-2.0/include']]
command += [str(vendor / f'libsigrok4DSL/{s}.c') for s in units]
command += [str(vendor / 'common' / p) for p in ['log/xlog.c', 'minizip/unzip.c', 'minizip/ioapi.c']]
owner_source = Path(__file__).resolve().parents[2] / 'tools/native/device-owner.c'
if args.direct_owner:
    command += ['-I' + str(owner_source.parent), str(owner_source)]
binary = root / 'bin/connection-probe.exe'
command += [str(source), '-L' + str(glib / 'lib'), '-L' + str(extra / 'lib'),
            '-lglib-2.0', '-lusb-1.0', '-lz', '-lwinpthread', '-o', str(binary)]
env = os.environ.copy()
env['PATH'] = str(compiler / 'bin') + os.pathsep + env['PATH']
manifest = {'revision': PIN, 'upstreamSources': records, 'command': command,
            'probeSource': str(source), 'probeSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
            'configSha256': hashlib.sha256(config.encode()).hexdigest(), 'scope': 'file-session-only'}
if args.direct_owner:
    manifest['ownerInputs'] = [{'path': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                               for p in [owner_source, owner_source.with_suffix('.h')]]
with (root / 'build.log').open('wb') as log:
    result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=120)
manifest['buildExit'] = result.returncode
if result.returncode == 0:
    manifest['binarySha256'] = hashlib.sha256(binary.read_bytes()).hexdigest()
(root / 'build-manifest.json').write_text(json.dumps(manifest, indent=2))
if result.returncode:
    raise SystemExit('Build failed; inspect ' + str(root / 'build.log'))
print('Built:', binary)
print('Upstream warnings are retained in:', root / 'build.log')

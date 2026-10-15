# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Build real DSLogic selection/factory code with a controlled USB transport.

Reuses the reviewed dependencies and source staging of a direct-owner probe build.
No physical device is opened by the resulting test executable.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
for name in ('base', 'upstream', 'compiler-root', 'glib-root', 'extra-root', 'output'):
    parser.add_argument('--' + name, type=Path, required=True)
args = parser.parse_args()
base = args.base.resolve(strict=True)
upstream = args.upstream.resolve(strict=True)
compiler, glib, extra = [p.resolve(strict=True) for p in (args.compiler_root, args.glib_root, args.extra_root)]
root = args.output.resolve(); root.mkdir(parents=True, exist_ok=False)
vendor = root / 'vendor'; (root / 'bin').mkdir()
previous = json.loads((base / 'build-manifest.json').read_text())
pin = '2e9e2c8e726df4ef5687d39b83d4f797cc44b574'
assert previous['revision'] == pin and previous['buildExit'] == 0 and 'ownerInputs' in previous
records = []
for item in previous['upstreamSources']:
    rel = Path(item['path'])
    assert not rel.is_absolute() and '..' not in rel.parts
    data = (base / 'vendor' / rel).read_bytes()
    assert hashlib.sha256(data).hexdigest() == item['sha256']
    dest = vendor / rel; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(data)
    records.append(item)
additional = ['hardware/DSL/' + p for p in ['dslogic.c', 'dsl.c', 'dsl.h', 'command.c', 'command.h']]
additional += ['hardware/common/usb.c']
for name in additional:
    rel = 'libsigrok4DSL/' + name
    data = subprocess.check_output(['git', 'show', pin + ':' + rel], cwd=upstream)
    dest = vendor / rel; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(data)
    records.append({'path': rel, 'sha256': hashlib.sha256(data).hexdigest()})
config = (base / 'vendor/libsigrok4DSL/config.h').read_bytes()
assert hashlib.sha256(config).hexdigest() == previous['configSha256']
(vendor / 'libsigrok4DSL/config.h').write_bytes(config)
repo = Path(__file__).resolve().parents[2]
native = repo / 'tools/native'
patch = native / 'dsl-connection-guards.patch'
subprocess.run(['git','apply','--ignore-space-change','--whitespace=nowarn',str(patch)],cwd=vendor,check=True)
patched = [{'path': name, 'sha256': hashlib.sha256((vendor / name).read_bytes()).hexdigest()}
           for name in ['libsigrok4DSL/hardware/DSL/dsl.c','libsigrok4DSL/hardware/DSL/command.c']]
unit = root / 'dslogic-selected-unit.c'
unit.write_text('#include "' + (vendor / 'libsigrok4DSL/hardware/DSL/dslogic.c').as_posix() + '"\n'
                '#include "' + (native / 'dslogic-selected.c').as_posix() + '"\n')
source = Path(__file__).with_name('selected-usb-probe.c').resolve()
units = ['backend','hwdriver','trigger','session_driver','session','dsdevice','strutil','std','log']
command = [str(compiler / 'bin/gcc.exe'), '-std=gnu11', '-O1', '-g', '-Wall', '-Wextra',
           '-ffunction-sections', '-fdata-sections', '-Wl,--gc-sections', '-static-libgcc']
command += ['-I' + str(p) for p in [native, vendor / 'libsigrok4DSL', vendor / 'common', extra / 'include',
            extra / 'include/libusb-1.0', glib / 'include/glib-2.0', glib / 'lib/glib-2.0/include']]
command += [str(vendor / f'libsigrok4DSL/{s}.c') for s in units]
command += [str(vendor / 'common' / p) for p in ['log/xlog.c','minizip/unzip.c','minizip/ioapi.c']]
command += [str(vendor / 'libsigrok4DSL' / p) for p in ['hardware/DSL/dsl.c','hardware/DSL/command.c','hardware/common/usb.c']]
command += [str(unit), str(native / 'selected-usb.c'), str(native / 'device-owner.c'), str(source)]
binary = root / 'bin/selected-usb-probe.exe'
command += ['-L' + str(glib / 'lib'), '-L' + str(extra / 'lib'), '-lglib-2.0','-lusb-1.0','-lz','-lwinpthread','-o',str(binary)]
env = os.environ.copy(); env['PATH'] = str(compiler / 'bin') + os.pathsep + env['PATH']
inputs = [source, unit, patch, *[native / n for n in ['selected-usb.c','selected-usb.h','dslogic-selected.c','dslogic-selected.h','device-owner.c','device-owner.h']]]
manifest = {'revision':pin,'scope':'real DSLogic factory with controlled USB fixture; no hardware acceptance',
            'upstreamSources':records,'patchedSources':patched,'command':command,'configSha256':previous['configSha256'],
            'inputs':[{'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in inputs]}
with (root / 'build.log').open('wb') as log:
    result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=120)
manifest['buildExit'] = result.returncode
if result.returncode == 0: manifest['binarySha256'] = hashlib.sha256(binary.read_bytes()).hexdigest()
(root / 'build-manifest.json').write_text(json.dumps(manifest,indent=2))
if result.returncode: raise SystemExit('Build failed; inspect ' + str(root / 'build.log'))
print('Built:',binary)

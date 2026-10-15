# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Real pinned lib_main/session-driver behavior, never a hardware acceptance test."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import queue
import shutil
import subprocess
import threading
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--direct-owner', action='store_true', help='Check direct product-owner behavior instead of global-manager behavior')
parser.add_argument('--worker', type=Path, required=True)
parser.add_argument('--fixture', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--runtime-dir', action='append', default=[])
args = parser.parse_args()
WORKER = args.worker.resolve(strict=True)
SOURCE = args.fixture.resolve(strict=True)
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == '9df041ddae3fd0873a0f327986528d1117c35cf9c4e31f91c4032b607f322cc7'
RUN = args.output.resolve()
RUN.mkdir(parents=True, exist_ok=False)
ENV = os.environ.copy()
ENV['PATH'] = os.pathsep.join([*args.runtime_dir, ENV['PATH']])

class Probe:
    def __init__(self, name, files):
        self.trace = []
        self.path = RUN / name
        self.path.mkdir()
        self.err = (self.path / 'stderr.txt').open('wb')
        self.p = subprocess.Popen([str(WORKER), *map(str, files)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.err, text=True, env=ENV)
        self.q = queue.Queue()
        self.reader = threading.Thread(target=self.drain, daemon=True)
        self.reader.start()
        try:
            self.registered = self.read()
            assert self.registered == {'event': 'registered', 'count': len(files)}
        except BaseException:
            self.abort()
            raise

    def drain(self):
        for line in self.p.stdout:
            self.q.put(line)
        self.q.put(None)

    def read(self):
        line = self.q.get(timeout=10)
        assert line is not None, f'Unexpected worker EOF; exit={self.p.poll()}'
        value = json.loads(line)
        self.trace.append(value)
        return value

    def command(self, value):
        assert self.p.poll() is None, 'Worker must remain live during a connection'
        self.trace.append({'command': value})
        self.p.stdin.write(value + '\n')
        self.p.stdin.flush()
        return self.read()

    def close(self, eof=False, bad_command=None):
        if eof:
            self.p.stdin.close()
            closed = self.read()
        else:
            closed = self.command(bad_command if bad_command is not None else 'close')
        expected = (4 if args.direct_owner else 5) if bad_command is not None else 0
        expected_closed = {'event': 'closed', 'result': expected, 'owned': False} if args.direct_owner else {'event': 'closed', 'cleanup': 0, 'result': expected}
        assert closed == expected_closed, closed
        assert self.p.wait(timeout=10) == expected
        self.reader.join(timeout=1)
        assert not self.reader.is_alive()
        self.save()

    def save(self):
        (self.path / 'trace.json').write_text(json.dumps(self.trace, indent=2))
        self.err.close()

    def abort(self):
        if self.p.poll() is None:
            self.p.kill()
            self.p.wait(timeout=10)
        self.reader.join(timeout=1)
        self.save()

def staged(name):
    path = RUN / name
    shutil.copyfile(SOURCE, path)
    return path

passes = []
active = None
def connected(value):
    return value['owned'] and value['result'] == 'ok' if args.direct_owner else value['connected']

try:
    a = staged('normal.demo')
    active = Probe('normal', [a])
    opened = active.command('open 0')
    # Values independently taken from installed fixture header, not from the adapter.
    assert connected(opened)
    if not args.direct_owner: assert opened['expected'] == opened['actual']
    assert (opened['sampleRate'], opened['sampleLimit' if args.direct_owner else 'sampleCount'], opened['channels']) == (25_000_000, 131072, list(range(16)))
    assert connected(active.command('read'))
    passes.append('Live file session returns exact device and real rate/count/physical channel readback')
    reopened = active.command('open 0')
    assert connected(reopened)
    if args.direct_owner:
        assert reopened['generation'] > opened['generation']
        assert active.command('read previous')['result'] == 'stale'
    else:
        assert reopened['actual'] == opened['actual'] and reopened['activations'] > opened['activations']
        assert active.command('read previous')['reason'] == 'stale-generation'
    passes.append('Reopening the same selection invalidates the prior operation generation')
    released = active.command('release')
    if args.direct_owner:
        assert not released['owned'] and active.command('read')['result'] == 'stale'
        saved = active.command('saved')
        assert not saved['owned'] and (saved['sampleRate'], saved['sampleLimit'], saved['channels']) == (25_000_000, 131072, list(range(16)))
        assert saved['snapshotGeneration'] == reopened['generation'] < released['generation']
        passes.append('Owned snapshot remains intact after native instance destruction without granting a live lease')
    else:
        assert released['api'] == 0 and released['actual'] == opened['actual'] and released['reason'] == 'not-active'
        assert not released['connected'] and active.command('read')['reason'] == 'no-lease'
    passes.append('Release revokes the lease and subsequent reads are rejected')
    active.close(); active = None
    passes.append('Close acknowledges native teardown and exits')

    a, b = staged('fallback-a.demo'), staged('fallback-b.demo')
    active = Probe('fallback', [a, b])
    first = active.command('open 0')
    assert connected(first)
    # Move only this test-owned file after actual native registration, before open.
    b.rename(b.with_suffix('.removed'))
    failed = active.command('open 1')
    if args.direct_owner:
        assert failed['native'] != 0 and failed['result'] == 'open-failed' and not failed['owned']
        assert failed['snapshotGeneration'] == 0 and failed['sampleRate'] == 0 and failed['channels'] == []
        assert active.command('read')['result'] == 'stale'
        assert active.command('read previous')['result'] == 'stale'
    else:
        assert failed['api'] == 0 and failed['actual'] == first['actual'] and failed['actual'] != failed['expected']
        assert not failed['connected'] and failed['reason'] == 'wrong-device'
        assert active.command('read')['reason'] == 'no-lease'
        assert active.command('read previous')['reason'] == 'stale-generation'
    active.close(); active = None
    passes.append('Failed switching preserves the original error without fallback or old lease' if args.direct_owner else 'Actual fallback reports API success on the wrong device; neither new nor old lease is published')

    a = staged('first-open-failure.demo')
    active = Probe('first-open-failure', [a])
    a.rename(a.with_suffix('.removed'))
    failed = active.command('open 0')
    if args.direct_owner:
        assert failed['native'] != 0 and failed['result'] == 'open-failed' and not failed['owned']
        assert failed['snapshotGeneration'] == 0 and failed['channels'] == []
    else:
        assert failed['api'] == 0 and failed['actual'] == failed['expected']
        assert not failed['connected'] and failed['reason'] == 'stale-activation' and failed['activations'] == 0
    active.close(); active = None
    passes.append('First-open failure is retained and the partially initialized instance is destroyed' if args.direct_owner else 'First-open failure can report success on the same partially opened instance; absent successful activation is rejected')

    a = RUN / 'zero-rate.demo'
    with zipfile.ZipFile(SOURCE) as z:
        header = z.read('header').replace(b'samplerate = 25 MHz', b'samplerate = 0 Hz')
        assert header != z.read('header')
    # Only metadata is used by this test; this intentionally invalid capture is
    # never submitted to acquisition or represented as valid waveform content.
    with zipfile.ZipFile(a, 'w') as z:
        z.writestr('header', header)
    active = Probe('zero-rate', [a])
    failed = active.command('open 0')
    if args.direct_owner:
        assert failed['native'] == 0 and failed['result'] == 'read-failed' and not failed['owned']
        assert failed['snapshotGeneration'] == 0 and failed['sampleRate'] == 0 and failed['channels'] == []
    else:
        assert failed['api'] == 0 and not failed['connected'] and failed['reason'] == 'invalid-settings'
    active.close(); active = None
    passes.append('Exact handle and active status cannot admit invalid sample-rate readback')

    active = Probe('eof', [staged('eof.demo')])
    assert connected(active.command('open 0'))
    active.close(eof=True); active = None
    passes.append('Parent command-pipe EOF tears down the active file session')
    active = Probe('unknown-command', [staged('unknown.demo')])
    assert connected(active.command('open 0'))
    active.close(bad_command='capture'); active = None
    passes.append('Unsupported control command fails and tears down without starting capture')
except BaseException:
    if active is not None: active.abort()
    raise
finally:
    (RUN / 'result.json').write_text(json.dumps({'passed': passes, 'count': len(passes)}, indent=2))
for label in passes: print('PASS', label)
print('Evidence:', RUN)

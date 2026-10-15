# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Controlled transport assertions for selected USB and real DSLogic failure paths."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--worker', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--runtime-dir', action='append', default=[])
args = parser.parse_args()
worker=args.worker.resolve(strict=True)
output=args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
env=os.environ.copy(); env['PATH']=os.pathsep.join([*args.runtime_dir,env['PATH']])

# Expected public outcome codes, not extracted from implementation source.
# selection, preparation, selected-device open attempts, firmware requests,
# control transfers, last driver error, preparation's native result.
cases={
    'ready':(0,0,1,0,0,0,0),
    'unknown-ports':(0,0,1,0,0,0,0),
    'missing':(2,-1,0,0,0,0,0),
    'ambiguous':(5,-1,0,0,0,0,0),
    'enumeration-failure':(6,-1,0,0,0,0,0),
    'descriptor-failure':(4,-1,0,0,0,0,0),
    'changed-revision':(0,2,0,0,0,0,0),
    'changed-port':(0,2,0,0,0,0,0),
    'disconnected':(0,5,0,0,0,0,0),
    'unknown-product':(0,3,0,0,0,0,0),
    'low-speed':(0,3,0,0,0,0,0),
    'access-denied':(0,4,1,0,0,0,-3),
    'string-failure':(0,5,1,0,0,0,-1),
    'firmware-required':(0,6,1,0,0,0,0),
    'firmware-allowed':(0,7,1,1,0,0,0),
    'firmware-failed':(0,8,1,1,0,0,1),
    'driver-open-denied':(0,0,2,0,0,11,0),
    'incompatible':(0,0,2,0,2,12,0),
    'short-pre':(0,0,2,0,1,13,0),
    'short-read':(0,0,2,0,2,13,0),
}
passed=[]
try:
    for name,expected in cases.items():
        run=subprocess.run([str(worker),name],stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,timeout=10)
        (output/(name+'.stdout')).write_bytes(run.stdout)
        (output/(name+'.stderr')).write_bytes(run.stderr)
        assert run.returncode==0,(name,run.returncode,run.stderr)
        data=json.loads(run.stdout)
        actual=(data['selected'],data['prepared'],data['opens'][0],data['firmware'][0],data['controls'],data['lastError'],data['native'])
        assert actual==expected,(name,actual,expected)
        assert data['opens'][1:]==[0,0] and data['firmware'][1:]==[0,0],(name,'Touched unselected device')
        assert data['references']==[1,1,1],(name,'Leaked or over-released USB reference')
        assert data['enumerations']==1 and data['claims']==0 and not data['owned'],(name,'Unexpected scan, claim or live owner')
        if expected[1]==0:
            assert data['candidate'] and data['channels']==16,(name,'Wrong selected native candidate')
        else:
            assert not data['candidate'] and data['channels']==0,(name,'False candidate')
        if name in ('driver-open-denied','incompatible','short-pre','short-read'):
            assert data['opened']==2 and data['openNative']==1,(name,'Original open failure was lost')
        else:
            assert data['opened']==-1,(name,'Candidate was opened unexpectedly')
        passed.append(name)
        print('PASS controlled selected-USB:',name)
finally:
    (output/'result.json').write_text(json.dumps({'passed':passed,'count':len(passed),
        'workerSha256':hashlib.sha256(worker.read_bytes()).hexdigest(),
        'scope':'Controlled USB transport, real DSLogic factory/driver; no physical hardware acceptance'},indent=2))

"""Bounded isolated full-app row regression; never invokes the normal-profile IPC."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--app', type=Path, required=True)
parser.add_argument('--fixture', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
for path in (args.app, args.fixture, args.report):
    if not path.is_absolute():
        parser.error('All paths must be absolute')
args.report.mkdir(parents=True, exist_ok=False)
(args.report / 'row-fixture.json').write_text(json.dumps({'Executable': str(args.fixture)}))
env = {key: value for key, value in os.environ.items() if not key.upper().startswith('DOTNET_ROOT')}
with subprocess.Popen([str(args.app), '--smoke-test', str(args.report)], cwd=args.app.parent, env=env) as app:
    deadline = time.monotonic() + 150
    captured = False
    while app.poll() is None and time.monotonic() < deadline:
        if not captured and (args.report / 'row-capture.ready').exists():
            print(f'CAPTURE_READY pid={app.pid}', flush=True)
            try:
                from PIL import ImageGrab
                ImageGrab.grab(all_screens=True).save(args.report / 'compositor-desktop.png')
            except Exception as error:
                (args.report / 'capture-error.txt').write_text(str(error))
            captured = True
        time.sleep(.1)
    if app.poll() is None:
        app.kill()
        app.wait()
        raise SystemExit('Regression exceeded 150-second bound')
    report = json.loads((args.report / 'smoke-test.json').read_text())
    print(json.dumps({'ExitCode': app.returncode, **report}, indent=2), flush=True)
    stress_path = args.report / 'row-stress.json'
    if stress_path.exists():
        stress = json.loads(stress_path.read_text())
        print(json.dumps({key: value for key, value in stress.items() if key != 'Snapshots'}, indent=2), flush=True)
    raise SystemExit(0 if app.returncode == 0 and report['Passed'] else 1)

"""Run the published app and actual dock Start buttons, without touching normal settings.
Global Explorer taskbar surfaces cannot be isolated from the normal app.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

p = argparse.ArgumentParser()
p.add_argument('--dotnet', required=True)
p.add_argument('--publish', required=True)
p.add_argument('--report', required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[1]
report = Path(a.report).resolve()
report.mkdir(parents=True, exist_ok=False)
fixture = root / 'tests/WinMac.Dock.Taskbar.Tests/bin/Debug/net10.0-windows/WinMac.Dock.Taskbar.Tests.dll'
settings = Path.home() / 'AppData/Local/WinMac/Dock/settings.json'
def digest():
    return hashlib.sha256(settings.read_bytes()).hexdigest() if settings.exists() else None

def processes():
    r = subprocess.run(['powershell', '-NoProfile', '-Command',
        "@(Get-Process -Name WinMac.Dock -ErrorAction SilentlyContinue | Select-Object Id,Path) | ConvertTo-Json -Compress"],
        capture_output=True, text=True, check=True)
    v = json.loads(r.stdout or '[]')
    return sorted(v if isinstance(v, list) else [v], key=lambda p: p['Id'])

def sample(name):
    target = report / name
    subprocess.run([a.dotnet, str(fixture), str(target), '--sample'], check=True)
    return json.loads((target / 'sample.json').read_text())

before_hash, before_processes, before_bars = digest(), processes(), sample('before')
(report / 'attention-fixture.json').write_text(json.dumps({'Dotnet': str(Path(a.dotnet).resolve()), 'Assembly': str(fixture)}))
env = {k: v for k, v in os.environ.items() if not k.startswith('DOTNET_ROOT')}
exe = Path(a.publish).resolve() / 'WinMac.Dock.exe'
app = subprocess.Popen([str(exe), '--smoke-test', str(report)], cwd=exe.parent, env=env)
error = None
click_code = None
try:
    deadline = time.monotonic() + 35
    while not (report / 'attention-start.ready').exists():
        if app.poll() is not None:
            raise RuntimeError('App exited before attention test: ' + str(app.returncode))
        if time.monotonic() > deadline:
            raise RuntimeError('Attention readiness marker timed out')
        time.sleep(.05)
    click = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        str(root / 'tests/VerifyDockStartClicks.ps1'), '-ReportDirectory', str(report),
        '-TargetProcessId', str(app.pid)], capture_output=True, text=True, timeout=20)
    (report / 'click-output.txt').write_text(click.stdout + click.stderr)
    click_code = click.returncode
    app.wait(timeout=50)
    smoke = json.loads((report / 'smoke-test.json').read_text())
    if click.returncode or app.returncode or not smoke['Passed']:
        raise RuntimeError(f'click={click.returncode}, app={app.returncode}, smoke={smoke}')
except Exception as ex:
    error = str(ex)
finally:
    if app.poll() is None:
        # Only the test process is owned. A forced exit intentionally retains recovery.
        app.kill()
        app.wait()
    after_bars, after_hash, after_processes = sample('after'), digest(), processes()
    result = dict(Passed=error is None and before_hash == after_hash and before_processes == after_processes
                  and before_bars == after_bars,
                  Error=error, AppExit=app.returncode, ClickExit=click_code,
                  SettingsHashBefore=before_hash, SettingsHashAfter=after_hash,
                  NormalProcessesBefore=before_processes, NormalProcessesAfter=after_processes,
                  AppearanceRestored=before_bars == after_bars,
                  GlobalTaskbarIsolation=False)
    (report / 'integration.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))
raise SystemExit(0 if result['Passed'] else 1)

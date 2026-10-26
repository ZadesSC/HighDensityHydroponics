import json
from pathlib import Path
import subprocess
import sys
result = subprocess.run(sys.argv[1:])
if result.returncode:
    for path in Path('Source').rglob('project.assets.json'):
        data = json.loads(path.read_text())
        print('RESTORE DIAGNOSTICS', path, json.dumps({'logs': data.get('logs'), 'restore': data.get('project', {}).get('restore'), 'frameworks': data.get('project', {}).get('frameworks')}, indent=2), flush=True)
    for path in Path('Source').rglob('project.nuget.cache'):
        print('CACHE DIAGNOSTICS', path, path.read_text(), flush=True)
raise SystemExit(result.returncode)

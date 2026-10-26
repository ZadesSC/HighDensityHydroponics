#!/usr/bin/env python3
"""Shared RimWorld package contract; standard library only, no game installation."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from zoneinfo import ZoneInfo

CONFIG = '.github/rimworld-build.json'
EXCLUDED = {'bin', 'obj', '__pycache__', '.git', 'Source', 'Souce', 'packages'}
DATE_VERSION = r'v\d{4}\.\d{2}\.\d{2}(?:\.[1-9]\d*)?'
RELEASE_VERSION = re.compile(DATE_VERSION + r'(?:-(alpha|beta|rc)\.[1-9]\d*)?')


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dump(value):
    return json.dumps(value, sort_keys=True, indent=2) + '\n'


def safe_path(root, name):
    relative = PurePosixPath(name)
    if not name or relative.is_absolute() or '..' in relative.parts or '\\' in name:
        raise ValueError(f'Unsafe relative path: {name}')
    path = root / name
    if path.is_symlink() or root.resolve() not in path.resolve().parents:
        raise ValueError(f'Path escapes package root or is a symlink: {name}')
    return path


def config(root):
    value = json.loads((root / CONFIG).read_text())
    if value['schemaVersion'] != 1 or not re.fullmatch(r'[A-Za-z][A-Za-z0-9]*', value['slug']):
        raise ValueError('Invalid schema version or mod slug')
    if value['assemblyMode'] not in ('compile', 'committed'):
        raise ValueError('assemblyMode must be compile or committed')
    return value


def source_hashes(root, cfg):
    files = set()
    for pattern in cfg.get('sourceGlobs', []):
        files.update(p for p in root.glob(pattern) if p.is_file()
                     and not {'bin', 'obj', 'packages', '__pycache__'} & set(p.relative_to(root).parts))
    return {p.relative_to(root).as_posix(): digest(p) for p in sorted(files)}


def fingerprint(root, cfg):
    return {'schemaVersion': 1, 'sources': source_hashes(root, cfg),
            'assemblies': {name: digest(safe_path(root, name)) for name in cfg['assemblies']},
            'provenance': 'Committed source/assembly baseline; not proof of compilation. '
                          'Refresh only after rebuilding and validating the assemblies.'}


def check_fingerprint(root, cfg):
    saved = json.loads((root / '.github/assembly-fingerprints.json').read_text())
    current = fingerprint(root, cfg)
    if saved['sources'] != current['sources'] or saved['assemblies'] != current['assemblies']:
        raise ValueError('Source or committed DLL changed. Rebuild/validate, then refresh '
                         '.github/assembly-fingerprints.json; do not blindly accept new hashes.')


def validate_version(version, channel):
    match = RELEASE_VERSION.fullmatch(version)
    if not match:
        raise ValueError('Use vYYYY.MM.DD[.N] or vYYYY.MM.DD[.N]-alpha.N/-beta.N/-rc.N')
    dt.datetime.strptime(version[1:11], '%Y.%m.%d')
    if bool(match.group(1)) != (channel == 'prerelease'):
        raise ValueError('Version suffix does not match the selected release channel')
    return version


def select_version(root, cfg, channel, requested, env=None, now=None):
    env = os.environ if env is None else env
    now = now or dt.datetime.now(ZoneInfo('America/Los_Angeles'))
    commit = git(root, 'rev-parse', 'HEAD')
    if channel == 'dev':
        return (f'dev.{now:%Y.%m.%d}.{env.get("GITHUB_RUN_NUMBER", "0")}.'
                f'{env.get("GITHUB_RUN_ATTEMPT", "1")}.g{commit[:12]}')
    event = env.get('GITHUB_EVENT_NAME', 'local')
    is_tag = event == 'push' and env.get('GITHUB_REF_TYPE') == 'tag'
    if event != 'local' and not is_tag:
        allowed = [cfg['defaultBranch']] + (['dev'] if channel == 'prerelease' else [])
        if env.get('GITHUB_REF_NAME') not in allowed:
            raise ValueError(f'{channel} releases must run from {allowed}')
    # A tag must point to a commit on the appropriate release branch.
    branch = 'dev' if channel == 'prerelease' and env.get('GITHUB_REF_NAME') == 'dev' else cfg['defaultBranch']
    if is_tag and channel == 'prerelease':
        candidates = [cfg['defaultBranch'], 'dev']
    else:
        candidates = [branch]
    if event != 'local':
        if not any(subprocess.run(['git', '-C', str(root), 'merge-base', '--is-ancestor',
                                   commit, 'origin/' + b], capture_output=True).returncode == 0
                   for b in candidates):
            raise ValueError('Release commit is outside the permitted release branches')
    tags = set(git(root, 'tag', '--list').splitlines())
    if is_tag:
        requested = env['GITHUB_REF_NAME']
    requested = requested.strip()
    if requested:
        version = validate_version(requested, channel)
        if version in tags:
            if not is_tag:
                raise ValueError(f'Tag already exists: {version}')
            if git(root, 'rev-parse', f'{version}^{{commit}}') != commit:
                raise ValueError('Release tag does not match the packaged commit')
        return version
    prefix = now.strftime('v%Y.%m.%d')
    if channel == 'prerelease':
        number = 1
        while f'{prefix}-beta.{number}' in tags:
            number += 1
        return f'{prefix}-beta.{number}'
    version, number = prefix, 0
    while version in tags:
        number += 1
        version = f'{prefix}.{number}'
    return version


def fetch_references(root, cfg, temp):
    """Download compile-time reference packages, never redistribute their DLLs."""
    for item in cfg.get('references', []):
        package, version = item['package'].lower(), item['version'].lower()
        url = f'https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg'
        archive_path = temp / (package + '.nupkg')
        with urllib.request.urlopen(url, timeout=90) as response:
            archive_path.write_bytes(response.read())
        destination = temp / item['destination']
        destination.mkdir(parents=True, exist_ok=True)
        count = 0
        with zipfile.ZipFile(archive_path) as archive:
            for name in archive.namelist():
                if name.startswith(item['prefix']) and name.endswith('.dll'):
                    (destination / PurePosixPath(name).name).write_bytes(archive.read(name))
                    count += 1
        if not count:
            raise ValueError(f'No reference assemblies in {url}: {item["prefix"]}')


def prepare(root, cfg):
    if cfg['assemblyMode'] == 'committed':
        check_fingerprint(root, cfg)
    else:
        if not cfg.get('commands'):
            raise ValueError('Compile mode requires a build command')
        for name in cfg['assemblies']:
            safe_path(root, name).unlink(missing_ok=True)
    with tempfile.TemporaryDirectory(prefix='rimworld-ci-') as directory:
        temp = Path(directory)
        fetch_references(root, cfg, temp)
        for command in cfg.get('commands', []):
            command = [part.replace('{references}', str(temp)) for part in command]
            print('+', ' '.join(command), flush=True)
            subprocess.run(command, cwd=root, check=True)


def collect_files(root, cfg):
    paths = set()
    for name in cfg['include']:
        path = safe_path(root, name)
        if not path.exists():
            raise ValueError(f'Missing required package input: {name}')
        paths.update(path.rglob('*') if path.is_dir() else [path])
    files = {}
    for path in sorted(paths):
        relative = path.relative_to(root).as_posix()
        if (EXCLUDED & set(PurePosixPath(relative).parts)
                or path.suffix.lower() in ('.pdb', '.mdb') or relative.endswith('.deps.json')):
            continue
        safe_path(root, relative)
        if path.is_file():
            if path.suffix.lower() == '.xml':
                ET.parse(path)
            if path.suffix.lower() == '.dll' and relative not in cfg['assemblies']:
                raise ValueError(f'Unexpected dependency or unlisted DLL: {relative}')
            files[relative] = path.read_bytes()
    required = ['About/About.xml', *cfg['assemblies']]
    for name in required:
        if name not in files or not files[name]:
            raise ValueError(f'Missing or empty runtime file: {name}')
    about = ET.fromstring(files['About/About.xml'])
    supported = [x.text for x in about.findall('supportedVersions/li')]
    if supported != cfg['supportedVersions']:
        raise ValueError('Supported versions differ between About.xml and build config')
    if 'LoadFolders.xml' in files:
        mappings = ET.fromstring(files['LoadFolders.xml'])
        if [node.tag for node in mappings] != ['v' + version for version in supported]:
            raise ValueError('LoadFolders.xml must explicitly map exactly the supported versions')
        for mapping in mappings:
            for node in mapping.findall('li'):
                folder = node.text
                if folder == '/':
                    continue
                safe_path(root, folder or '')
                if not any(name.startswith(folder.rstrip('/') + '/') for name in files):
                    raise ValueError(f'Load folder is absent from ZIP: {folder}')
    return files, about


def package(root, cfg, version, channel, env=None):
    env = os.environ if env is None else env
    if channel != 'dev':
        validate_version(version, channel)
    elif not re.fullmatch(r'dev\.\d{4}\.\d{2}\.\d{2}\.\d+\.\d+\.g[0-9a-f]{12}', version):
        raise ValueError('Invalid development build identity')
    files, about = collect_files(root, cfg)
    commit = git(root, 'rev-parse', 'HEAD')
    pr = {}
    event_file = env.get('GITHUB_EVENT_PATH')
    if event_file and Path(event_file).is_file():
        pr = json.loads(Path(event_file).read_text()).get('pull_request', {})
    info = {'schemaVersion': 1, 'mod': cfg['slug'], 'version': version,
            'channel': channel, 'commit': commit, 'ref': env.get('GITHUB_REF', git(root, 'branch', '--show-current')),
            'event': env.get('GITHUB_EVENT_NAME', 'local'), 'supportedVersions': cfg['supportedVersions'],
            'modVersion': about.findtext('modVersion'), 'assemblyMode': cfg['assemblyMode'],
            'assemblyProvenance': ('Compiled from source in this run' if cfg['assemblyMode'] == 'compile'
                                   else 'Committed DLLs checked against source/assembly fingerprint baseline'),
            'dependencies': cfg.get('references', []) + cfg.get('dependencyNotes', []),
            'assemblySha256': {name: hashlib.sha256(files[name]).hexdigest() for name in cfg['assemblies']},
            'toolingCommit': env.get('RIMWORLD_TOOLING_REF', 'local'),
            'prNumber': pr.get('number'), 'prHeadCommit': pr.get('head', {}).get('sha'),
            'runId': env.get('GITHUB_RUN_ID'), 'runAttempt': env.get('GITHUB_RUN_ATTEMPT')}
    if env.get('GITHUB_RUN_ID'):
        info['runUrl'] = (f'{env["GITHUB_SERVER_URL"]}/{env["GITHUB_REPOSITORY"]}'
                          f'/actions/runs/{env["GITHUB_RUN_ID"]}')
    files['BUILD_INFO.json'] = dump(info).encode()
    output = root / 'dist'
    # Only our generated dist directory is cleared, after any legacy validation/build steps.
    if output.is_symlink():
        raise ValueError('dist must not be a symlink')
    if output.exists():
        shutil.rmtree(output)
    output.mkdir()
    archive_path = output / f'{cfg["slug"]}-{version}.zip'
    with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            entry = zipfile.ZipInfo(f'{cfg["slug"]}/{name}', date_time=(2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, data)
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip() or set(archive.namelist()) != {cfg['slug'] + '/' + name for name in files}:
            raise ValueError('ZIP verification failed')
        for name, data in files.items():
            if archive.read(cfg['slug'] + '/' + name) != data:
                raise ValueError(f'ZIP entry verification failed: {name}')
    info.update(package=archive_path.name, packageSha256=digest(archive_path), fileCount=len(files))
    (output / 'BUILD_INFO.json').write_text(dump(info))
    # Cover both the installable ZIP and external build metadata.
    (output / 'SHA256SUMS').write_text(''.join(f'{digest(p)}  {p.name}\n'
                                            for p in [archive_path, output / 'BUILD_INFO.json']))
    notes = [f'RimWorld: {", ".join(cfg["supportedVersions"])}.',
             f'Install: extract `{archive_path.name}` into your RimWorld Mods directory.',
             'Replace the existing mod folder before installing; keep only one copy enabled.',
             f'Commit: `{commit}`. Assembly mode: `{cfg["assemblyMode"]}`.',
             'Required mods: ' + ', '.join(x.findtext('displayName') or x.findtext('packageId') or ''
                                           for x in about.findall('modDependencies/li')) + '.',
             'Download the named mod ZIP; GitHub source archives are not the installable package.']
    (root / 'release-notes.txt').write_text('\n\n'.join(notes) + '\n')
    print(f'Verified {archive_path.name}: {len(files)} files, SHA256 {info["packageSha256"]}')
    return info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['select-version', 'build', 'fingerprint'])
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--channel', choices=['dev', 'stable', 'prerelease'], default='dev')
    parser.add_argument('--version', default='')
    args = parser.parse_args()
    root = args.root.resolve()
    cfg = config(root)
    if args.operation == 'fingerprint':
        (root / '.github/assembly-fingerprints.json').write_text(dump(fingerprint(root, cfg)))
        return
    if args.operation == 'select-version':
        version = select_version(root, cfg, args.channel, args.version)
        if os.environ.get('GITHUB_OUTPUT'):
            with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
                output.write(f'version={version}\nartifact={cfg["slug"]}-{version}\n')
                output.write(f'needs-dotnet={str(cfg.get("needsDotnet", False)).lower()}\n')
        print(version)
        return
    prepare(root, cfg)
    version = args.version or select_version(root, cfg, args.channel, '')
    package(root, cfg, version, args.channel)


if __name__ == '__main__':
    main()

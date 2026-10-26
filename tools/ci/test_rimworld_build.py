"""Contract tests for release safety and the installable package."""
import datetime as dt
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import rimworld_build as build


class BuildContract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        subprocess.run(['git', 'init', '-q', str(self.root)], check=True)
        subprocess.run(['git', '-C', str(self.root), 'config', 'user.email', 'ci@example.invalid'], check=True)
        subprocess.run(['git', '-C', str(self.root), 'config', 'user.name', 'CI test'], check=True)
        for name, data in {
            'About/About.xml': '<ModMetaData><name>Example</name><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>',
            'LoadFolders.xml': '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>',
            '1.7/Assemblies/Example.dll': 'fake DLL',
            'Source/Example.cs': '// source',
            '.github/placeholder': '',
        }.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(data)
        subprocess.run(['git', '-C', str(self.root), 'add', '.'], check=True)
        subprocess.run(['git', '-C', str(self.root), 'commit', '-qm', 'fixture'], check=True)
        self.cfg = {'slug': 'Example', 'defaultBranch': 'main', 'supportedVersions': ['1.7'],
                    'include': ['About', 'LoadFolders.xml', '1.7'], 'assemblyMode': 'committed',
                    'assemblies': ['1.7/Assemblies/Example.dll'], 'sourceGlobs': ['Source/**/*.cs']}
        self.now = dt.datetime(2026, 10, 26)

    def test_calendar_versions_and_channels(self):
        for version, channel in [('v2026.10.26', 'stable'), ('v2026.10.26.1', 'stable'),
                                 ('v2026.10.26-beta.1', 'prerelease')]:
            self.assertEqual(build.validate_version(version, channel), version)
        for version, channel in [('v2026.02.30', 'stable'), ('v0.2.0', 'stable'),
                                 ('v2026.10.26-beta.1', 'stable'), ('v2026.10.26', 'prerelease'),
                                 ('v2026.10.26.0', 'stable'), ('v2026.10.26;echo x', 'stable')]:
            with self.assertRaises(ValueError):
                build.validate_version(version, channel)

    def test_same_day_allocation_and_explicit_collision(self):
        for tag in ['v2026.10.26', 'v2026.10.26.1', 'v2026.10.26-beta.1']:
            subprocess.run(['git', '-C', str(self.root), 'tag', tag], check=True)
        self.assertEqual(build.select_version(self.root, self.cfg, 'stable', '', {}, self.now), 'v2026.10.26.2')
        self.assertEqual(build.select_version(self.root, self.cfg, 'prerelease', '', {}, self.now), 'v2026.10.26-beta.2')
        with self.assertRaisesRegex(ValueError, 'already exists'):
            build.select_version(self.root, self.cfg, 'stable', 'v2026.10.26', {}, self.now)

    def test_stable_release_rejects_dev_branch(self):
        with self.assertRaisesRegex(ValueError, 'must run from'):
            build.select_version(self.root, self.cfg, 'stable', '',
                                 {'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF_NAME': 'dev'}, self.now)

    def test_tag_release_requires_permitted_ancestry(self):
        with self.assertRaisesRegex(ValueError, 'outside'):
            build.select_version(self.root, self.cfg, 'stable', '',
                                 {'GITHUB_EVENT_NAME': 'push', 'GITHUB_REF_TYPE': 'tag',
                                  'GITHUB_REF_NAME': 'v2026.10.26'}, self.now)

    def test_source_change_invalidates_fingerprint(self):
        (self.root / '.github/assembly-fingerprints.json').write_text(build.dump(build.fingerprint(self.root, self.cfg)))
        build.check_fingerprint(self.root, self.cfg)
        (self.root / 'Source/Example.cs').write_text('// changed')
        with self.assertRaisesRegex(ValueError, 'Source or committed DLL changed'):
            build.check_fingerprint(self.root, self.cfg)

    def test_dll_change_invalidates_fingerprint(self):
        (self.root / '.github/assembly-fingerprints.json').write_text(build.dump(build.fingerprint(self.root, self.cfg)))
        (self.root / '1.7/Assemblies/Example.dll').write_text('changed DLL')
        with self.assertRaisesRegex(ValueError, 'Source or committed DLL changed'):
            build.check_fingerprint(self.root, self.cfg)

    def test_compile_cannot_silently_use_an_old_dll(self):
        self.cfg.update(assemblyMode='compile', commands=[['git', 'status', '--porcelain']])
        build.prepare(self.root, self.cfg)
        with self.assertRaisesRegex(ValueError, 'Missing or empty runtime'):
            build.collect_files(self.root, self.cfg)

    def test_download_version_does_not_leak_into_msbuild(self):
        self.cfg.update(assemblyMode='compile', commands=[['python3', '-c',
                        'import os; assert not any(k.lower() == "version" for k in os.environ)']])
        with patch.dict(os.environ, {'VERSION': 'dev.2026.10.26.1.1.gabcdef123456'}):
            build.prepare(self.root, self.cfg)

    def test_dependency_dll_cannot_be_redistributed(self):
        (self.root / '1.7/Assemblies/0Harmony.dll').write_text('dependency')
        with self.assertRaisesRegex(ValueError, 'Unexpected dependency'):
            build.collect_files(self.root, self.cfg)

    def test_load_folder_must_exist_in_package(self):
        (self.root / 'LoadFolders.xml').write_text('<loadFolders><v1.7><li>Missing</li></v1.7></loadFolders>')
        with self.assertRaisesRegex(ValueError, 'absent from ZIP'):
            build.collect_files(self.root, self.cfg)

    def test_supported_versions_must_match(self):
        self.cfg['supportedVersions'] = ['1.6']
        with self.assertRaisesRegex(ValueError, 'Supported versions differ'):
            build.collect_files(self.root, self.cfg)

    def test_symlinks_and_traversal_are_rejected(self):
        for name in ['../secret', '/etc/passwd', 'Source\\file']:
            with self.assertRaises(ValueError):
                build.safe_path(self.root, name)
        (self.root / '1.7/linked.xml').symlink_to(self.root / 'About/About.xml')
        with self.assertRaises(ValueError):
            build.collect_files(self.root, self.cfg)

    def test_reproducible_zip_and_build_identity(self):
        env = {'GITHUB_RUN_NUMBER': '42', 'GITHUB_RUN_ATTEMPT': '2'}
        version = build.select_version(self.root, self.cfg, 'dev', '', env, self.now)
        self.assertRegex(version, r'^dev\.2026\.10\.26\.42\.2\.g[0-9a-f]{12}$')
        first = build.package(self.root, self.cfg, version, 'dev', env)
        archive = self.root / 'dist' / first['package']
        original = archive.read_bytes()
        second = build.package(self.root, self.cfg, version, 'dev', env)
        self.assertEqual(original, archive.read_bytes())
        self.assertEqual(first['packageSha256'], second['packageSha256'])
        with zipfile.ZipFile(archive) as bundle:
            self.assertTrue(all(name.startswith('Example/') for name in bundle.namelist()))
            self.assertFalse(any(Path(name).name.upper() == 'BUILD_INFO.JSON' for name in bundle.namelist()))
        internal = json.loads((self.root / '.rimworld-ci/build-info.json').read_text())
        self.assertEqual(internal['commit'], build.git(self.root, 'rev-parse', 'HEAD'))
        self.assertEqual(internal['packageSha256'], first['packageSha256'])
        self.assertEqual({p.name for p in (self.root / 'dist').iterdir()}, {first['package'], 'SHA256SUMS'})
        sums = (self.root / 'dist/SHA256SUMS').read_text()
        self.assertIn(first['packageSha256'] + '  ' + first['package'], sums)
        self.assertEqual(len(sums.splitlines()), 1)

    def test_legacy_build_info_is_excluded_from_allowed_folders(self):
        (self.root / 'About/BUILD_INFO.json').write_text('{"old": true}')
        files, _ = build.collect_files(self.root, self.cfg)
        self.assertNotIn('About/BUILD_INFO.json', files)


if __name__ == '__main__':
    unittest.main()

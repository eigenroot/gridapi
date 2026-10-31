import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
COMMIT = 'a' * 40
IMAGE = 'ghcr.io/eigenroot/gridapi'
TOOLS = '''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
name = Path(sys.argv[0]).name
args = sys.argv[1:]
if name == 'timeout': os.execvp(args[1], args[1:])
if name == 'flock': raise SystemExit(0)
if name == 'curl':
    print('{"status":"unhealthy"}' if os.environ.get('FAIL_HEALTH') else '{"status":"ok"}')
    raise SystemExit(0)
with open(os.environ['TOOL_LOG'], 'a') as stream:
    stream.write(json.dumps(args) + '\\n')
if args[0] == 'login':
    sys.stdin.read()
    if os.environ.get('FAIL_LOGIN'): raise SystemExit(7)
if '--Database:MigrateOnly=true' in args and os.environ.get('FAIL_MIGRATION'):
    raise SystemExit(8)
'''


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='grid-deploy-test-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        tools = self.root / 'bin'
        tools.mkdir()
        for name in ('docker', 'curl', 'timeout', 'flock'):
            path = tools / name
            path.write_text(TOOLS)
            path.chmod(0o700)
        self.original = (
            'GRID_API_IMAGE=ghcr.io/janprince/gridapi:previous\n'
            'ConnectionStrings__Grid=Host=private;Password=secret-$-value;Database=grid\n'
            'Payouts__EncryptionKey=preserve-this-key\n'
            'Database__MigrateOnStartup=false\n'
        )
        (self.root / '.env').write_text(self.original)
        (self.root / 'compose.yml').write_text('services: {}\n')
        self.env = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ['PATH'],
                        GRID_DEPLOY_ROOT=str(self.root), GRID_DEPLOY_HEALTH_DELAY='0',
                        GRID_DEPLOY_HEALTH_ATTEMPTS='2', TOOL_LOG=str(self.root / 'calls'))

    def deploy(self, **overrides):
        result = subprocess.run(['bash', str(ROOT / 'deploy/deploy-release.sh'),
                                 IMAGE, COMMIT, 'v0.1.0', 'janprince'],
                                env=dict(self.env, **overrides), input='registry-secret-token',
                                capture_output=True, text=True)
        self.assertNotIn('registry-secret-token', result.stdout + result.stderr)
        self.assertNotIn('secret-$-value', result.stdout + result.stderr)
        self.assertEqual([], [p for p in self.root.glob('.deploy.*') if p.is_dir()])
        return result

    def calls(self):
        return [json.loads(line) for line in (self.root / 'calls').read_text().splitlines()]

    def test_success_preserves_credentials_and_records_exact_release(self):
        result = self.deploy()
        self.assertEqual(0, result.returncode, result.stderr)
        env = (self.root / '.env').read_text()
        self.assertIn(f'GRID_API_IMAGE={IMAGE}:{COMMIT}', env)
        self.assertIn('Password=secret-$-value', env)
        self.assertIn('Payouts__EncryptionKey=preserve-this-key', env)
        self.assertIn('Database__MigrateOnStartup=false', env)
        self.assertEqual(0o600, (self.root / '.env').stat().st_mode & 0o777)
        release = json.loads((self.root / '.release.json').read_text())
        self.assertEqual(COMMIT, release['commit'])
        self.assertEqual('v0.1.0', release['version'])
        calls = self.calls()
        migration = next(i for i, c in enumerate(calls) if '--Database:MigrateOnly=true' in c)
        start = next(i for i, c in enumerate(calls) if 'up' in c)
        self.assertLess(migration, start)

    def test_failed_health_restores_previous_image_and_configuration(self):
        result = self.deploy(FAIL_HEALTH='1')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(self.original, (self.root / '.env').read_text())
        self.assertFalse((self.root / '.release.json').exists())
        self.assertEqual(2, sum('up' in c for c in self.calls()))
        self.assertIn('Database migrations are not automatically reversed', result.stderr)

    def test_failed_migration_keeps_previous_image(self):
        result = self.deploy(FAIL_MIGRATION='1')
        self.assertEqual(8, result.returncode)
        self.assertEqual(self.original, (self.root / '.env').read_text())
        self.assertFalse((self.root / '.release.json').exists())

    def test_registry_failure_does_not_change_server_configuration(self):
        result = self.deploy(FAIL_LOGIN='1')
        self.assertEqual(7, result.returncode)
        self.assertEqual(self.original, (self.root / '.env').read_text())
        self.assertFalse(any('up' in c for c in self.calls()))

    def test_duplicate_image_setting_aborts_and_restores_file(self):
        original = self.original + 'GRID_API_IMAGE=duplicate\n'
        (self.root / '.env').write_text(original)
        result = self.deploy()
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(original, (self.root / '.env').read_text())
        self.assertFalse(any('--Database:MigrateOnly=true' in c for c in self.calls()))

    def test_invalid_image_is_rejected_before_docker_runs(self):
        result = subprocess.run(['bash', str(ROOT / 'deploy/deploy-release.sh'),
                                 'invalid;image', COMMIT, 'v0.1.0', 'janprince'],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertFalse((self.root / 'calls').exists())


RELEASE_TOOLS = '''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
path = Path(os.environ['RELEASE_STATE'])
state = json.loads(path.read_text())
args = sys.argv[1:]
if Path(sys.argv[0]).name == 'docker':
    state.setdefault('docker', []).append(args)
elif args[0] == 'api' and '--paginate' in args:
    for tag in state['tags']: print(tag['name'] + '\\t' + tag['sha'])
elif args[0] == 'api' and '--method' in args:
    values = dict(arg.split('=', 1) for arg in args if arg.startswith(('ref=', 'sha=')))
    if state.pop('conflict', False):
        state['tags'].append({'name': 'v0.1.0', 'sha': 'b' * 40})
        path.write_text(json.dumps(state))
        raise SystemExit(1)
    state['tags'].append({'name': values['ref'].split('/')[-1], 'sha': values['sha']})
elif args[:2] == ['release', 'view']:
    raise SystemExit(0 if args[2] in state['releases'] else 1)
elif args[:2] == ['release', 'create']:
    state['releases'].append(args[2])
else: raise SystemExit('Unexpected command')
path.write_text(json.dumps(state))
'''


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='grid-release-test-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        for name in ('gh', 'docker'):
            path = self.root / name
            path.write_text(RELEASE_TOOLS)
            path.chmod(0o700)
        self.state = self.root / 'state'
        self.state.write_text(json.dumps({'tags': [], 'releases': []}))

    def release(self):
        env = dict(os.environ, PATH=str(self.root) + os.pathsep + os.environ['PATH'],
                   GITHUB_REPOSITORY='eigenroot/gridapi', GITHUB_SHA=COMMIT,
                   GITHUB_OUTPUT=str(self.root / 'output'), RELEASE_STATE=str(self.state))
        result = subprocess.run(['python3', str(ROOT / 'deploy/create-release.py')],
                                env=env, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        return json.loads(self.state.read_text())

    def test_first_release_is_v0_1_0_and_has_matching_image_tag(self):
        state = self.release()
        self.assertEqual(['v0.1.0'], state['releases'])
        self.assertIn(['push', IMAGE + ':v0.1.0'], state['docker'])
        self.assertIn('version=v0.1.0', (self.root / 'output').read_text())

    def test_rerun_reuses_existing_release(self):
        self.release()
        state = self.release()
        self.assertEqual(1, len(state['tags']))
        self.assertEqual(['v0.1.0'], state['releases'])

    def test_next_merge_increments_patch_version(self):
        self.state.write_text(json.dumps({'tags': [{'name': 'v1.2.3', 'sha': 'b' * 40}],
                                          'releases': ['v1.2.3']}))
        state = self.release()
        self.assertEqual(['v1.2.3', 'v1.2.4'], state['releases'])

    def test_concurrent_tag_reservation_retries_with_next_version(self):
        self.state.write_text(json.dumps({'tags': [], 'releases': [], 'conflict': True}))
        state = self.release()
        self.assertEqual(['v0.1.1'], state['releases'])


if __name__ == '__main__': unittest.main()

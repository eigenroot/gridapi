"""Reserve a stable version tag, tag the tested image, and publish release notes."""
import json
import os
import re
import subprocess
import time

repo = os.environ['GITHUB_REPOSITORY']
commit = os.environ['GITHUB_SHA']
image = 'ghcr.io/' + repo.lower()
pattern = re.compile(r'^v(\d+)\.(\d+)\.(\d+)$')

def run(*args, check=True):
    return subprocess.run(args, check=check, capture_output=True, text=True)

version = None
for attempt in range(5):
    tags = run('gh', 'api', '--paginate', f'repos/{repo}/tags?per_page=100',
               '--jq', '.[] | [.name, .commit.sha] | @tsv').stdout.splitlines()
    stable = []
    for line in tags:
        name, sha = line.split('\t')
        match = pattern.fullmatch(name)
        if match: stable.append((tuple(map(int, match.groups())), name, sha))
    existing = [tag for tag in stable if tag[2] == commit]
    if existing:
        version = max(existing)[1]
        break
    if stable:
        major, minor, patch = max(stable)[0]
        candidate = f'v{major}.{minor}.{patch + 1}'
    else:
        candidate = 'v0.1.0'
    # GitHub reserves refs atomically, preventing concurrent merges claiming one tag.
    result = run('gh', 'api', '--method', 'POST', f'repos/{repo}/git/refs',
                 '-f', f'ref=refs/tags/{candidate}', '-f', f'sha={commit}', check=False)
    if result.returncode == 0:
        version = candidate
        break
    if attempt == 4: raise SystemExit(result.stderr)
    time.sleep(2)
if version is None: raise SystemExit('Unable to reserve a release version.')

for args in [
    ('docker', 'pull', f'{image}:{commit}'),
    ('docker', 'tag', f'{image}:{commit}', f'{image}:{version}'),
    ('docker', 'push', f'{image}:{version}'),
]:
    subprocess.run(args, check=True)
if run('gh', 'release', 'view', version, '--repo', repo, check=False).returncode != 0:
    subprocess.run(['gh', 'release', 'create', version, '--repo', repo,
                    '--verify-tag', '--title', version, '--generate-notes'], check=True)
with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
    stream.write(f'version={version}\nimage={image}\n')
print(f'Release {version} uses {image}:{commit}')

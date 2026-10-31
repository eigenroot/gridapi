#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if [[ $# != 4 ]]; then
    echo 'Usage: deploy-release.sh IMAGE COMMIT VERSION REGISTRY_USER' >&2
    exit 2
fi
image=$1
commit=$2
version=$3
registry_user=$4
if [[ ! $image =~ ^ghcr\.io/[a-z0-9._-]+/[a-z0-9._-]+$ ]] ||
   [[ ! $commit =~ ^[0-9a-f]{40}$ ]] ||
   [[ ! $version =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo 'Invalid image, commit, or release version.' >&2
    exit 2
fi

root=${GRID_DEPLOY_ROOT:-/opt/grid}
health_url=${GRID_DEPLOY_HEALTH_URL:-https://grid-api.eigenroot.com/health}
attempts=${GRID_DEPLOY_HEALTH_ATTEMPTS:-30}
delay=${GRID_DEPLOY_HEALTH_DELAY:-2}
cd "$root"
test -f .env
test -f compose.yml
exec 9> .deploy.lock
flock -w 300 9

workdir=$(mktemp -d "$root/.deploy.XXXXXXXX")
export DOCKER_CONFIG="$workdir/docker"
mkdir -m 700 "$DOCKER_CONFIG"
activated=0
compose() { docker compose --project-directory "$root" -f "$root/compose.yml" "$@"; }
cleanup() {
    status=$?
    trap - EXIT
    if (( status != 0 && activated == 1 )); then
        echo 'Deployment failed; restoring the previous API image.' >&2
        cp "$workdir/previous.env" "$root/.env"
        chmod 600 "$root/.env"
        if ! compose up -d --no-deps api; then
            echo 'Previous image could not be restarted; inspect the API logs.' >&2
        fi
        echo 'Database migrations are not automatically reversed.' >&2
    fi
    rm -rf -- "$workdir"
    exit "$status"
}
trap cleanup EXIT

# The job token arrives on stdin and is never echoed or put in process arguments.
docker login ghcr.io --username "$registry_user" --password-stdin
docker pull "$image:$commit"
cp .env "$workdir/previous.env"
activated=1

python3 - "$root/.env" "$image:$commit" <<'PY'
import os, sys, tempfile
from pathlib import Path
path = Path(sys.argv[1])
lines = path.read_text().splitlines()
matches = [i for i, line in enumerate(lines) if line.startswith('GRID_API_IMAGE=')]
if len(matches) != 1:
    raise SystemExit('Expected exactly one GRID_API_IMAGE setting.')
lines[matches[0]] = 'GRID_API_IMAGE=' + sys.argv[2]
# Normal API starts never perform schema changes; the one-off command below does.
matches = [i for i, line in enumerate(lines) if line.startswith('Database__MigrateOnStartup=')]
if len(matches) > 1:
    raise SystemExit('Duplicate Database__MigrateOnStartup setting.')
if matches:
    lines[matches[0]] = 'Database__MigrateOnStartup=false'
else:
    lines.append('Database__MigrateOnStartup=false')
fd, temporary = tempfile.mkstemp(prefix='.env.', dir=path.parent)
try:
    with os.fdopen(fd, 'w') as stream:
        stream.write('\n'.join(lines) + '\n')
    os.replace(temporary, path)
finally:
    if os.path.exists(temporary): os.unlink(temporary)
PY

compose config --quiet
timeout 180 docker compose --project-directory "$root" -f "$root/compose.yml" \
    run --rm --no-deps api --Database:MigrateOnly=true
compose up -d --no-deps api

for (( attempt=1; attempt<=attempts; attempt++ )); do
    if curl --fail --silent --show-error --max-time 10 "$health_url" > "$workdir/health.json" &&
       python3 - "$workdir/health.json" <<'PY'
import json, sys
with open(sys.argv[1]) as stream:
    if json.load(stream).get('status') != 'ok': raise SystemExit(1)
PY
    then
        python3 - "$root/.release.json" "$version" "$commit" "$image" <<'PY'
import datetime, json, sys
from pathlib import Path
path = Path(sys.argv[1])
if path.exists(): path.with_name('.release.previous.json').write_bytes(path.read_bytes())
path.write_text(json.dumps({
    'version': sys.argv[2], 'commit': sys.argv[3], 'image': sys.argv[4] + ':' + sys.argv[3],
    'deployedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
}, indent=2) + '\n')
PY
        activated=0
        printf 'Deployed %s (%s); HTTPS health check passed.\n' "$version" "$commit"
        exit 0
    fi
    sleep "$delay"
done
echo 'The HTTPS health check did not pass.' >&2
exit 1

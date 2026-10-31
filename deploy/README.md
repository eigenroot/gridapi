# Deploy GRID API on a DigitalOcean Droplet

Docker Engine and Compose must already be installed. This deploys only the .NET
API and Caddy; the Flutter apps are built and distributed separately.

## Deployment flow

Source code lives in GitHub. A build on your Mac or GitHub Actions produces a
Linux AMD64 image, publishes it to a container registry, and the Droplet pulls
that image. The release workflow uses the repository's namespace, currently
`ghcr.io/eigenroot/gridapi`. The server does not need a source-code pull to deploy
an image.

## Automatic releases and deployment

The workflow builds and tests pull requests. Each successful update to `main`
(normally a PR merge) then:

1. Builds and publishes a Linux AMD64 image tagged with its exact commit SHA.
2. Reserves the next stable version, beginning at `v0.1.0` and increasing the
   patch number, publishes that image tag, and creates a GitHub Release with
   generated notes. A rerun of the same commit reuses its version.
3. Uploads the deployment script over SSH, pulls the image, applies EF migrations
   in a one-off container, recreates only the API, and checks public HTTPS health.

Release tag reservation handles concurrent merges. Production deployments are
serialized; an older commit skips deployment when a newer main commit already
exists. A failure before deployment prevents the rollout. A failure during
migration or API startup restores the previous API image and `.env`. Database
migrations are not automatically reversed: schema changes must remain compatible
with the preceding application release. A GitHub Release records a tested image;
check its workflow's deployment result to see whether it reached production.

Configure these repository Actions secrets:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | `64.227.34.236` |
| `DEPLOY_USER` | `gridadmin` |
| `DEPLOY_SSH_KEY` | Dedicated deployment private key |
| `DEPLOY_KNOWN_HOSTS` | Trusted SSH host-key entries from an already verified connection |

Authorize the public key in the deployment user's `~/.ssh/authorized_keys` and
grant that user Docker access. Docker-group membership grants administrator-level
control of the server. GitHub sends its short-lived package token over encrypted
SSH stdin so private organization images can be pulled; the temporary registry
credentials are deleted at the end. Production database and SMS credentials stay
in `/opt/grid/.env` and are never copied into GitHub or the image.

The server needs Bash, Python 3, curl, `flock`, `timeout`, Docker and Compose.
These utilities are present in the Ubuntu 24.04 setup used here. The script
preserves application secrets, uploads, and Caddy's certificate volumes. Its
release metadata is in `/opt/grid/.release.json`.

Normal API restarts keep `Database__MigrateOnStartup=false`. The deployment's
one-off `--Database:MigrateOnly=true` command exits after EF migrations, without
starting the HTTP server, seeding development accounts, or running KYC workers.

After transferring the repository to Eigenroot, update source-checkout remotes:

```sh
git -C /opt/grid/source remote set-url origin https://github.com/eigenroot/gridapi.git
```

The running image can remain under the previous namespace until the first
successful release from Eigenroot replaces it.

Run the image build from the `Grid.API` directory (where Dockerfile lives):

```sh
docker buildx build --platform linux/amd64 --load -t grid-api:beta .
```

Appsettings files, environment files, and local uploads are excluded from the
build context. Supply production settings on the server, not in the image.

## First release without a registry

If GitHub and a registry are not configured yet, transfer a built image directly.
Run these commands on the Mac, from the `Grid.API` directory:

```sh
docker save -o /private/tmp/grid-api-beta.tar grid-api:beta
scp /private/tmp/grid-api-beta.tar gridadmin@64.227.34.236:~/grid-api-beta.tar
ssh gridadmin@64.227.34.236 'sudo install -d -m 750 -o gridadmin -g gridadmin /opt/grid'
scp compose.production.yml gridadmin@64.227.34.236:/opt/grid/compose.yml
scp deploy/Caddyfile gridadmin@64.227.34.236:/opt/grid/Caddyfile
scp deploy/.env.example gridadmin@64.227.34.236:/opt/grid/.env.example
```

In the Droplet's SSH session:

```sh
sudo docker load -i ~/grid-api-beta.tar
cd /opt/grid
cp -n .env.example .env
chmod 600 .env
nano .env
```

Set `GRID_API_IMAGE=grid-api:beta`, then fill every domain and credential value.
For each of the three application secrets, run `openssl rand -base64 32` separately
on the server and paste the result into the corresponding field in the editor.
Do not send secrets in chat or commit `.env`.

## Database, DNS, and start

Finish linking Neon and use its direct .NET/Npgsql connection string for this
initial single-instance deployment, because startup also applies EF migrations.
Do not copy development database rows or development signing keys.

Create a DNS A record for the chosen API hostname pointing at `64.227.34.236`.
It must resolve to this server and allow inbound TCP 80/443 for Caddy HTTPS.
If using Cloudflare DNS, use DNS-only mode for the initial HTTPS check.

When `.env` is complete, run on the server:

```sh
cd /opt/grid
sudo docker compose config --quiet
sudo docker compose up -d
sudo docker compose ps
sudo docker compose logs --tail=100 api caddy
```

Then check the chosen hostname:

```sh
curl --fail https://YOUR_API_DOMAIN/health
```

Expected response: `{"status":"ok"}`. Also test real OTP delivery, sign-in,
profile/home persistence, and uploads from a device before distributing builds.

After the first successful migration, change `Database__MigrateOnStartup=false`
in `.env` and run `sudo docker compose up -d` to recreate the API container.

## Manual deployment and recovery

Use an immutable version tag or digest in `GRID_API_IMAGE`, then:

```sh
cd /opt/grid
sudo docker compose pull
sudo docker compose up -d
sudo docker compose ps
```

For a private GHCR image, authenticate the server using `sudo docker login
ghcr.io` and a token with read access to the package before pulling. The build
workflow needs permission to publish to that package. The automatic workflow
provides a short-lived token during its deployment; a separate manual pull may
need its own login. A manual image rollback does not reverse database migrations.

## Persistent state

Only Caddy publishes ports. API port 8080 stays on Docker's private network,
where Caddy supplies forwarding headers. Docker-published ports can bypass UFW.
The deployment intentionally does not publish a database port.

Files currently use the `media` Docker volume because R2 is not implemented in
the API yet. Recreating a container keeps this volume; destroying the Droplet
does not. Back up uploads off-server before accepting important documents. Do not
use `docker compose down -v` unless you intend to delete uploads and TLS state.

The Neon database, the server `.env` (especially the payout encryption key), and
the uploads each need their own recovery plan. KYC polling is off initially.
There is no periodic database health probe, so idle Neon compute can suspend.

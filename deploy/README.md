# Deploy GRID API on a DigitalOcean Droplet

Docker Engine and Compose must already be installed. This deploys only the .NET
API and Caddy; the Flutter apps are built and distributed separately.

## Deployment flow

Source code lives in GitHub. A build on your Mac or GitHub Actions produces a
Linux AMD64 image, publishes it to a container registry, and the Droplet pulls
that image. The Droplet does not automatically pull GitHub source or deploy a
release just because Docker is installed. The registry and build workflow must
be configured after the repository is selected.

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

## Later releases from a registry

Use an immutable version tag or digest in `GRID_API_IMAGE`, then:

```sh
cd /opt/grid
sudo docker compose pull
sudo docker compose up -d
sudo docker compose ps
```

For a private GHCR image, authenticate the server using `sudo docker login
ghcr.io` and a token with read access to the package before pulling. The build
workflow needs permission to publish to that package. Pushing a commit alone
does not update the Droplet; run these deployment commands, or explicitly add a
deployment job once the manual process is verified.

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

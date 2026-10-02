# Deploy Workout on Unraid

This deployment uses the existing PostgreSQL database and Google client. The React app and API run together in one container. A migration job must finish successfully before the app starts. Cloudflare Tunnel supplies HTTPS without publishing an inbound app port.

## Requirements

- Unraid Docker enabled and `docker compose version` working. If Compose is missing, install Compose Manager through Community Applications, then verify the command again.
- A Cloudflare-managed hostname and remotely managed tunnel, or an existing HTTPS reverse proxy configured for this app.
- Reachability from Unraid to PostgreSQL at `192.168.0.48:5432`.
- A private copy of the configured laptop `.env`. Do not use `secrets.txt` as the app configuration: the app uses its dedicated `workout` database connection from `.env`.

The commands below use Cloudflare Tunnel. An existing reverse proxy requires an explicit network configuration and trusted proxy address before starting the app.

## First deployment

1. Clone `https://github.com/swaene90/workout.git` into `/mnt/user/appdata/workout/source`. Keep the source checkout separate from persistent keys.
2. Transfer the laptop `.env` privately into that checkout. Restrict it with `chmod 600 .env`. Set `PUBLIC_HOSTNAME` to your hostname without a scheme or path, and supply the tunnel token in `TUNNEL_TOKEN`. Keep the database and Google credentials already configured.
3. Add `https://YOUR_HOSTNAME/signin-google` to the Google web client's authorized redirect URIs. Keep the localhost callback for laptop use.
4. Configure the Cloudflare tunnel public hostname to route to `http://app:8080`. The tunnel service must run in this Compose stack so the app can trust its internal address.
5. Check that `172.30.0.0/24` does not overlap an existing Docker/LAN network. If it does, change the subnet, static container addresses, and `Proxy__TrustedIp` together in a server-specific override before deployment.
6. Prepare the key directory and start the stack from the source checkout:

```sh
cd /mnt/user/appdata/workout/source
docker compose -f compose.yaml -f compose.unraid.yaml build
docker compose -f compose.yaml -f compose.unraid.yaml run --rm --no-deps --user root --entrypoint sh app -c 'chown app:app /keys && chmod 700 /keys'
docker compose -f compose.yaml -f compose.unraid.yaml --profile remote up -d --wait
docker compose -f compose.yaml -f compose.unraid.yaml --profile remote ps -a
```

The key directory defaults to `/mnt/user/appdata/workout/keys`. Set `WORKOUT_APPDATA_PATH` in `.env` if your appdata share uses another path. Preserve and privately back up this directory; it keeps session cookies usable after container restarts. Do not copy laptop keys over an existing server's keys.

The migration container should exit with code 0, the app should be healthy, and the tunnel should be running. Open `https://YOUR_HOSTNAME`, sign in with Google, and confirm check-ins, detailed logging, themes, and histories. The demo is available at `/demo`.

Do not use `compose.local.yaml` on Unraid: it switches to development cookies and binds the app to the server's loopback address. This production stack does not expose port 5080. Users access the HTTPS hostname.

## Reusing an existing Cloudflare tunnel

When an existing `cloudflared` container already serves other applications, attach Workout to its Docker network with `compose.shared-tunnel.yaml`. Do not start Workout's own tunnel service or use the `remote` profile in this mode.

Set `TUNNEL_NETWORK` to that network's name and `TUNNEL_PROXY_IP` to the existing tunnel container's IPv4 address in the private `.env`. The server's current tunnel is on `fantfoot_default` at `172.21.0.3`. The proxy address must remain stable; reserve it in the tunnel's own deployment configuration when recreating that container. If its address changes, update `TUNNEL_PROXY_IP` and recreate Workout. Only that address is trusted for forwarded headers.

Configure the Cloudflare hostname route to HTTP service `http://workout-app:8080`, and run:

```sh
docker compose -f compose.yaml -f compose.unraid.yaml -f compose.shared-tunnel.yaml up --build -d --wait
```

Use the same three files for subsequent updates and inspection. The migration job, appdata key preparation, Google callback, and backups described above still apply. No tunnel token is copied into Workout's configuration when reusing the existing tunnel.

## Reboots and updates

The app and tunnel use Docker's `unless-stopped` restart policy. Keep Docker enabled at Unraid startup. If using Compose Manager, manage this as one Compose stack and retain both Compose files and the `remote` profile. Avoid independently starting the migration container through Unraid's ordinary container autostart controls.

Before updating, back up the database and keys as described in [operations.md](operations.md). Then:

```sh
cd /mnt/user/appdata/workout/source
git pull --ff-only
docker compose -f compose.yaml -f compose.unraid.yaml --profile remote up --build -d --wait
```

For startup failures, inspect only locally; logs can contain connection details:

```sh
docker compose -f compose.yaml -f compose.unraid.yaml logs --tail 100 migrate app
docker compose -f compose.yaml -f compose.unraid.yaml --profile remote logs --tail 100 tunnel
```

See [operations.md](operations.md) for database provisioning, backup, rollback, and authentication details.

# Set up Workout as a Progressive Web App

Workout remains a website hosted by the existing .NET container. Its manifest supplies the app name, icons, and standalone launch mode. The service worker caches the static app shell and displays push notifications. IndexedDB stores account-specific cached screens and pending workout mutations. Installation does not require Apple Developer membership.

## 1. Deploy the website over HTTPS

Use [the Unraid deployment guide](unraid.md) for initial database, Google OAuth, Cloudflare Tunnel, and persistent session-key setup. Keep the same public hostname and Google callback (`https://YOUR_HOSTNAME/signin-google`). No native OAuth client is needed for this web app.

On the Unraid server, from `/mnt/user/appdata/workout/source`:

```sh
git pull --ff-only
docker compose -f compose.yaml -f compose.unraid.yaml build
docker compose -f compose.yaml -f compose.unraid.yaml --profile remote up -d --wait
```

For an existing shared tunnel, use the deployment guide's alternative:

```sh
docker compose -f compose.yaml -f compose.unraid.yaml -f compose.shared-tunnel.yaml up --build -d --wait
```

The migration service applies the workout revision, mutation receipt, notification preference, subscription, job, and delivery tables before the app starts. Back up PostgreSQL and persistent session keys before updating; use [operations.md](operations.md). Do not point migration or test commands at a database you intend to discard without checking its identity.

## 2. Configure optional Web Push

Installation and offline workouts work without push configuration. To enable notifications, generate a VAPID key pair **once** on your server:

```sh
docker compose -f compose.yaml -f compose.unraid.yaml run --rm --no-deps app --generate-vapid
```

This deliberately prints a public/private key pair. Run it privately; do not paste the output into chat, logs, Git, or screenshots. Copy the values into the server's private `.env`:

```dotenv
Notifications__Enabled=true
Notifications__PublicKey=YOUR_GENERATED_PUBLIC_KEY
Notifications__PrivateKey=YOUR_GENERATED_PRIVATE_KEY
Notifications__Subject=mailto:YOUR_REAL_CONTACT_EMAIL
```

Then recreate the stack using the same deployment command from step 1. The subject must be a real contact email (`mailto:`) or HTTPS contact URL. Missing keys keep notifications unavailable without breaking workouts. Set `Notifications__Enabled=false` and recreate the app to stop sending notifications.

Back up the VAPID keys with your private server configuration and retain them across updates. Replacing the keys requires devices to disable notifications, then enable them again to subscribe with the new public key. Back up PostgreSQL to preserve preferences, subscriptions, pending server jobs, and mutation receipts.

The server must have outbound HTTPS access to browser push services, including `*.push.apple.com`, `fcm.googleapis.com`, Mozilla Push, and Windows Push. Endpoint validation accepts these providers only, rejects non-HTTPS destinations, and checks public DNS addresses before connecting. Delivery uses `Lib.Net.Http.WebPush` with current `aes128gcm` payload encryption. No Firebase account is needed for standards-based browser push.

## 3. Verify the PWA deployment

From the public HTTPS website, verify:

- `/manifest.webmanifest` returns JSON with `application/manifest+json`, a stable `id`, `/` start URL/scope, `standalone` display, and PNG icons.
- `/sw.js` returns JavaScript, not the SPA HTML fallback. Its root path gives it root scope.
- The icon URLs in the manifest and `/icons/apple-touch-icon.png` load.
- `sw.js`, the manifest, and HTML revalidate rather than being permanently cached. Hashed build assets are precached; old build caches are removed during worker activation.
- Authentication paths, Google callbacks, health checks, and API requests are excluded from navigation fallback and never put in the service-worker cache. Personal data lives in account-scoped IndexedDB; CSRF tokens are not persisted.
- On an iPhone, follow [the installation guide](install-on-iphone.md), verify standalone launch, and sign in with an invited Google account.
- Load Workouts, enter airplane mode, create a workout, edit an existing loaded workout, and queue a deletion. Close/reopen the app and verify pending entries remain.
- Reconnect and reopen Workout. Pending changes should sync once; conflicts show both versions and offer a choice.
- Enable notifications in Settings and verify a partner check-in appears with the app closed and the phone locked. Tap it to open the overview.
- Deploy an update while an unsynced workout exists. Finish editing before selecting Update app; pending saved changes must survive.

## Local development and tests

Ordinary development uses `scripts/dev.ps1`. Service-worker generation is part of the production build, so use a built preview when testing it:

```powershell
cd frontend
npm ci
npm run test
npm run build
npm run preview
```

The preview serves frontend files only; use the full Docker stack to verify authenticated APIs, offline sync, and push together. The repository includes bundled runtimes under `.tools` if Node/.NET are not on your PATH. Run backend tests with `dotnet test backend/Workout.Tests/Workout.Tests.csproj`.

Desktop localhost is a secure-context exception, but an iPhone connecting to a LAN HTTP address is not. Test iPhone installation and push on the public HTTPS origin. Do not expose local development servers or database ports publicly to make this work.

## Behavior and troubleshooting

- **Cannot install:** Open the public site in Safari, use Share → Add to Home Screen, and check the manifest and icons. On recent iOS versions, enable Open as Web App if offered.
- **Notifications unavailable:** Check the four push environment variables, enabled flag, HTTPS, iOS version, and Home Screen installation. Browser support is feature-detected.
- **Permission denied:** Change permission in iPhone Settings → Notifications → Workout. The app cannot repeatedly force the system prompt.
- **Not receiving alerts:** Check category switches, quiet hours, subscription state, server uptime, and outbound connectivity. Disable/re-enable notifications to renew an expired subscription. Logs report queue counts and retry attempts without endpoint secrets or workout contents.
- **Stale app:** Reopen while online and use Update app after finishing edits. Do not clear website data while pending workouts exist.
- **Sign-in fails:** Confirm the Google callback matches the public origin and the account is invited. Test login from the installed app itself; do not assume Safari's cookies are shared.
- **Pending sync:** Reopen online and use Retry sync. Expired sessions require sign-in to the same account. Conflicts and validation errors preserve your local data; review the sync panel before applying or discarding it.

Reminder defaults use New York time: daily 6 PM, Sunday nudge 5 PM, quiet hours 9 PM–8 AM. Reminders skip confirmed check-ins/met goals; successfully sent Sunday nudges suppress the later daily reminder. Partner activity and celebrations wait until quiet hours end and expire after 24 hours. Scheduled reminders and nudges expire after 15 minutes to avoid stale alerts. Delivery is best effort, subject to iOS, Focus, and connectivity.

The server cannot see an unsynced offline workout and may still send a reminder. iPhone background synchronization is not assumed: reopen the app with internet to sync. Offline edits are local until confirmed by the server. Phone storage can be cleared or evicted, so synchronize regularly and before removing the app.

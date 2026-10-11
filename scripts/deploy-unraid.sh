#!/usr/bin/env bash
set -euo pipefail

# Only the trusted main-branch push job invokes this on the production runner.
: "${GITHUB_SHA:?Set the commit that passed CI}"
: "${WORKOUT_DEPLOY_PATH:=/mnt/user/appdata/workout/source}"
if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Invalid deployment commit." >&2
  exit 1
fi

cd -- "$WORKOUT_DEPLOY_PATH"
deploy_path="$(pwd -P)"
git_source() { git -c "safe.directory=$deploy_path" "$@"; }
if [[ "$(git_source rev-parse --show-toplevel)" != "$deploy_path" ]]; then
  echo "Deployment path must be the root of the Workout checkout." >&2
  exit 1
fi
if [[ -n "$(git_source status --porcelain)" ]]; then
  echo "The server checkout has local changes. Resolve them before deploying." >&2
  exit 1
fi
if [[ ! -f .env ]]; then
  echo "The server checkout needs its private .env configuration." >&2
  exit 1
fi

git_source fetch origin main
if [[ "$(git_source rev-parse origin/main)" != "$GITHUB_SHA" ]]; then
  echo "Skipping an outdated deployment; a newer main commit will deploy after its checks pass."
  exit 0
fi
git_source checkout --detach "$GITHUB_SHA"

# Keep using the existing shared Cloudflare tunnel; no FanFoot services are restarted.
compose=(docker compose -f compose.yaml -f compose.unraid.yaml -f compose.shared-tunnel.yaml)
"${compose[@]}" config --quiet
"${compose[@]}" build app
# A one-off migration runs on every release, even if an older migrate container exited successfully.
# A failed build or migration stops here, before the existing app is recreated.
"${compose[@]}" run --rm --no-deps migrate
"${compose[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 120 app
curl --fail --silent --show-error --output /dev/null --retry 5 --retry-delay 2 \
  "https://${PUBLIC_HOSTNAME:-workout.swaenepoel.org}/health/ready"
echo "Workout deployed successfully: $GITHUB_SHA"

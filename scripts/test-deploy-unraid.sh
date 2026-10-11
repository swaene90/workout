#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
deployment_script="$script_dir/deploy-unraid.sh"
bash -n "$deployment_script"
temp_root="$(cd -- "${TMPDIR:-/tmp}" && pwd -P)"
test_dir="$(mktemp -d "$temp_root/workout-deploy-tests.XXXXXXXX")"
cleanup() {
  # Only remove the temporary directory created by this test, beneath its resolved temp root.
  if [[ "$test_dir" == "$temp_root"/workout-deploy-tests.* && -d "$test_dir" ]]; then
    rm -rf -- "$test_dir"
  fi
}
trap cleanup EXIT
mkdir -p "$test_dir/bin" "$test_dir/source"
touch "$test_dir/source/.env"
export WORKOUT_DEPLOY_PATH="$test_dir/source"
export GITHUB_SHA=1111111111111111111111111111111111111111
export TEST_HEAD="$GITHUB_SHA" TEST_LOG="$test_dir/commands" TEST_DIRTY="" TEST_FAIL_MIGRATE=0
cat > "$test_dir/bin/git" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
shift 2 # Per-command safe.directory option.
case "$*" in
  'rev-parse --show-toplevel') cd "$WORKOUT_DEPLOY_PATH"; pwd -P ;;
  'status --porcelain') printf '%s' "$TEST_DIRTY" ;;
  'rev-parse origin/main') printf '%s\n' "$TEST_HEAD" ;;
  *) printf 'git %s\n' "$*" >> "$TEST_LOG" ;;
esac
EOF
cat > "$test_dir/bin/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf 'docker %s\n' "$*" >> "$TEST_LOG"
if [[ "$*" == *'run --rm --no-deps migrate'* && "$TEST_FAIL_MIGRATE" == 1 ]]; then exit 1; fi
EOF
cat > "$test_dir/bin/curl" <<'EOF'
#!/usr/bin/env bash
printf 'curl %s\n' "$*" >> "$TEST_LOG"
EOF
chmod +x "$test_dir/bin/"*
export PATH="$test_dir/bin:$PATH"
touch "$TEST_LOG"

# Happy path: deploy exactly the verified SHA, migrate before restart, and check the public route.
bash "$deployment_script"
grep -q "checkout --detach $GITHUB_SHA" "$TEST_LOG"
migration_line="$(grep -n 'run --rm --no-deps migrate' "$TEST_LOG" | cut -d: -f1)"
restart_line="$(grep -n 'up -d --no-deps --force-recreate --wait' "$TEST_LOG" | cut -d: -f1)"
[[ "$migration_line" -lt "$restart_line" ]]
grep -q 'https://workout.swaenepoel.org/health/ready' "$TEST_LOG"

# Failed migrations must never restart the currently serving app.
: > "$TEST_LOG"
export TEST_FAIL_MIGRATE=1
if bash "$deployment_script"; then echo "Expected migration failure." >&2; exit 1; fi
! grep -q 'up -d' "$TEST_LOG"
! grep -q '^curl ' "$TEST_LOG"

# An older CI run cannot overwrite a newer main release.
: > "$TEST_LOG"
export TEST_FAIL_MIGRATE=0 TEST_HEAD=2222222222222222222222222222222222222222
bash "$deployment_script"
! grep -q '^docker ' "$TEST_LOG"
! grep -q 'checkout' "$TEST_LOG"

# Refuse to overwrite local server changes.
: > "$TEST_LOG"
export TEST_DIRTY=' M compose.yaml'
if bash "$deployment_script"; then echo "Expected dirty checkout refusal." >&2; exit 1; fi
[[ ! -s "$TEST_LOG" ]]
echo "Deployment orchestration tests passed."

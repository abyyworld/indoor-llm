#!/usr/bin/env bash
# Sync my own work to github.com/abyyworld/indoor-llm.
#
# That repo is PUBLIC and this one is not. Mengkai's research/ folder holds her
# unpublished thesis material: supervision notes, formative testing, the paper outline.
# Publishing any of it would be hers to decide, not mine, so the export below is an
# allowlist rather than a denylist. A denylist fails open when a new folder appears;
# this fails closed.
#
# CONTRIBUTORS.md is also excluded. It carries her own description of her contribution
# and is a shared document, so it stays in the shared repo.
#
#   ./sync-to-mine.sh              dry run, prints what would be pushed
#   ./sync-to-mine.sh --push       actually push
#
# The shared repo is unaffected either way. Push there with a normal git push.

set -euo pipefail

REPO="$(pwd)"

REMOTE="mine"
BRANCH="main"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Allowlist. Anything not named here never leaves this machine.
INCLUDE=(
  pipeline
  unity
  configs
  tests
  logs
  README.md
  CLAUDE.md
  design-spec.md
  build-decisions.md
  proposals-for-review.md
  study-design-v2.md
  requirements.txt
  .gitignore
  sync-to-mine.sh
  test-participant.sh
  serve-study.py
  web

)

echo "Staging export..."
for item in "${INCLUDE[@]}"; do
  if [ -e "$item" ]; then
    mkdir -p "$WORK/$(dirname "$item")"
    cp -R "$item" "$WORK/$(dirname "$item")/"
  fi
done

# Belt and braces: if research/ ever reaches the export, stop rather than publish it.
if [ -d "$WORK/research" ]; then
  echo "REFUSING: research/ is in the export. That is Mengkai's unpublished work." >&2
  exit 1
fi

# Real participant logs are data, not code. Keep only the worked example.
find "$WORK/logs" -name '*.csv' ! -name 'EXAMPLE_*' -delete 2>/dev/null || true

# Unity build cache. cp does not read .gitignore, and unity/Library is ~750 files of
# rebuildable junk that has no business in a public repository.
rm -rf "$WORK/unity/Library" "$WORK/unity/Logs" "$WORK/unity/UserSettings" \
       "$WORK/unity/Temp" "$WORK/unity/obj" "$WORK/unity/Build" "$WORK/unity/Builds" 2>/dev/null || true

# Build artefacts. .gitignore covers them in the shared repo but cp does not read it.
find "$WORK" -name '__pycache__' -type d -prune -exec rm -rf {} + 2>/dev/null || true
find "$WORK" -name '.DS_Store' -delete 2>/dev/null || true
find "$WORK" -name '*.pyc' -delete 2>/dev/null || true

echo
echo "Would push $(find "$WORK" -type f | wc -l | tr -d ' ') files:"
# `| head` closes the pipe on `find`, which under `set -o pipefail` aborts the whole
# script -- silently, after printing the listing and before pushing anything. It only
# started biting once the export grew past forty files. Sort to a variable first so
# nothing is reading from a pipe that gets closed underneath it.
# sed, not head: head closes the pipe when it has enough, and under pipefail that
# aborts the whole script before it pushes anything. It looked like a successful run.
LISTING="$(find "$WORK" -type f | sed "s|$WORK/|  |" | sort)"
TOTAL="$(printf '%s\n' "$LISTING" | wc -l | tr -d ' ')"
printf '%s\n' "$LISTING" | sed -n '1,40p'
[ "$TOTAL" -gt 40 ] && echo "  ... and $((TOTAL - 40)) more"
true
echo

if [ "${1:-}" != "--push" ]; then
  echo "Dry run. Re-run with --push to publish."
  exit 0
fi

CLONE="$(mktemp -d)"
trap 'rm -rf "$WORK" "$CLONE"' EXIT

# Commit ON TOP of whatever is already on indoor-llm rather than replacing it, so no
# force push and nothing already there is discarded. The export is a snapshot, but the
# history it lands in is preserved.
echo "Cloning indoor-llm..."
git clone -q --branch "$BRANCH" https://github.com/abyyworld/indoor-llm.git "$CLONE"

# Replace tracked content with the export. Files removed here become real deletions in
# the commit, which is what keeps the two repos in step.
( cd "$CLONE" && git rm -rq --ignore-unmatch . )
cp -R "$WORK"/. "$CLONE"/

cd "$CLONE"
git add -A
if git diff --cached --quiet; then
  echo "Nothing changed since the last sync."
  exit 0
fi

git -c user.name="$(git -C "$REPO" config user.name)" \
    -c user.email="$(git -C "$REPO" config user.email)" \
    commit -q -m "${SYNC_MESSAGE:-Sync from working repo}"
git push "$REMOTE" "$BRANCH" 2>/dev/null || git push origin "$BRANCH"
echo "Pushed to indoor-llm."

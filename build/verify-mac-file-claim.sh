#!/usr/bin/env bash
# Verify, on a real Mac, that Launch Services types a board as Pivotask's own document.
#
# This used to be the open question proposal 0004 §4 left unanswered, back when a board was called
# `.pivotask.json`: a two-part extension, which CFBundleTypeExtensions cannot match (it sees only the
# final component), so the claim rode on an exported UTI carrying the whole two-part tag. The format
# has since been renamed to a plain `.pivotask` — still JSON inside, but with a name of its own — which
# is exactly the kind of extension an operating system routes without any special pleading.
#
# So this is now a check rather than an experiment. It still exists as a script because the devcontainer
# is Linux and no amount of reading settles what a Mac actually does.
#
#   ./build/verify-mac-file-claim.sh                     # uses artifacts/pivotask/<rid>/Pivotask.app
#   ./build/verify-mac-file-claim.sh path/to/Pivotask.app
#
# On PASS: nothing to do.
# On FAIL: apply §10.3. Delete the CFBundleDocumentTypes *and* UTExportedTypeDeclarations arrays from
#          features/ManageTasks/Pivotask.Desktop/Info.plist and ship with no Finder claim. Do NOT fall
#          back to claiming bare `json`: that puts Pivotask in the Open With menu of every JSON file on
#          the machine, most of which it cannot parse.
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This has to run on macOS — that is the entire point of it." >&2
  exit 2
fi

LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister

app="${1:-}"
if [[ -z "$app" ]]; then
  app=$(find artifacts/pivotask -maxdepth 2 -name Pivotask.app -type d 2>/dev/null | head -1 || true)
fi

if [[ -z "$app" || ! -d "$app" ]]; then
  echo "No Pivotask.app found. Build one first:  just publish-pivotask-app" >&2
  exit 2
fi

echo "Registering $app with Launch Services…"
"$LSREGISTER" -f "$app"

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
sample="$scratch/sample.pivotask"
printf '{\n  "version": 1,\n  "groups": []\n}\n' > "$sample"

# Spotlight decides a file's type from the same Uniform Type Identifier database Launch Services uses,
# so this is the question asked directly: what type does the system think this file is?
echo "Asking the system what sample.pivotask is…"
for _ in $(seq 1 10); do
  type=$(mdls -raw -name kMDItemContentType "$sample" 2>/dev/null || echo "")
  [[ -n "$type" && "$type" != "(null)" ]] && break
  sleep 0.5
done

echo "  kMDItemContentType = ${type:-<none>}"
echo "  Handler:"
"$LSREGISTER" -dump | grep -B2 -A6 'com\.pivotask\.board' | sed 's/^/    /' || echo "    (not in the database)"

if [[ "$type" == "com.pivotask.board" ]]; then
  echo
  echo "PASS — a .pivotask file is typed as com.pivotask.board and opens in Pivotask."
  exit 0
fi

echo
echo "FAIL — the system typed it as '${type:-nothing}', not com.pivotask.board."
echo "Apply proposal 0004 §10.3: remove CFBundleDocumentTypes and UTExportedTypeDeclarations from"
echo "features/ManageTasks/Pivotask.Desktop/Info.plist entirely. Do not claim bare 'json'."

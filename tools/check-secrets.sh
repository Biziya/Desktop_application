#!/bin/sh
# Fails when a file about to be committed (default) or any tracked file (--all) holds a filled-in connection string
# or password. Used by .githooks/pre-commit and the CI workflow. Placeholders such as Password=... are allowed.
# Real connection strings belong in Backend/appsettings.Local.json, which git ignores.

pattern='"DefaultConnection"[[:space:]]*:[[:space:]]*"[^"<.][^"]*"|(password|pwd)[[:space:]]*=[[:space:]]*[^.;"'"'"'[:space:]<$]'

if [ "$1" = "--all" ]; then
  files=$(git ls-files)
else
  files=$(git diff --cached --name-only --diff-filter=ACMR)
fi

found=0
for f in $files; do
  [ -f "$f" ] || continue
  # The test fixture's fake password is not a secret.
  hits=$(grep -I -n -i -E "$pattern" "$f" 2>/dev/null | grep -v 'NotARealPassword')
  if [ -n "$hits" ]; then
    echo "$hits" | cut -d: -f1 | sed "s|^|  $f line |"
    found=1
  fi
done

if [ $found -ne 0 ]; then
  echo ""
  echo "Blocked: the lines above look like a real connection string or password."
  echo "Put it in Backend/appsettings.Local.json (git-ignored) instead, and leave the committed file empty."
  exit 1
fi

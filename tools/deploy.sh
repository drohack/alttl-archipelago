#!/usr/bin/env bash
#
# Build the plugins and put them in the game folder.
#
# Closes the game first, always. The deploy step is a file copy into
# BepInEx/plugins, and Windows will not overwrite a DLL that a running process
# has loaded - so building with the game open compiles cleanly, fails only in
# the copy, and leaves the OLD plugin in place. That cost a full round of
# testing against a build that did not contain the fix being tested, with a
# green "0 Error(s)" on screen the whole time.
#
# Exits non-zero on ANY error, including MSB3021/MSB3027 copy failures.
set -euo pipefail

GAME_DIR="G:/Games/Steam/steamapps/common/A Little To The Left"
PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/ALTTLArchipelago"

powershell -NoProfile -Command \
  "Get-Process | Where-Object {\$_.ProcessName -like '*Little*'} | Stop-Process -Force" \
  >/dev/null 2>&1 || true

# Give Windows time to release the file handles the process held.
for _ in 1 2 3 4 5 6 7 8 9 10; do
    powershell -NoProfile -Command \
      "@(Get-Process | Where-Object {\$_.ProcessName -like '*Little*'}).Count" \
      2>/dev/null | grep -q '^0' && break
    sleep 1
done

for project in src/ALTTLArchipelago src/ALTTLDevTools; do
    echo "-- building $project --"
    if ! dotnet build "$project" -c Debug --nologo -v q; then
        echo "BUILD FAILED: $project"
        exit 1
    fi
done

echo "-- deployed --"
ls -la --time-style=+%H:%M:%S "$PLUGIN_DIR/ALTTLArchipelago.dll"

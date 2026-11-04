#!/bin/sh
set -eu

if [ "$#" -ne 0 ]; then
    echo "Usage: ./scripts/android-main.sh" >&2
    exit 1
fi

repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_dir="$repo_dir/artifacts/android-main-source"
mobile_dir="$source_dir/src/Host/OperationsSystem.Mobile"

if [ ! -e "$source_dir/.git" ]; then
    git -C "$repo_dir" worktree add --detach "$source_dir" main
else
    if [ -n "$(git -C "$source_dir" status --porcelain --untracked-files=no)" ]; then
        echo "The isolated main Android checkout has tracked changes; preserve them before rebuilding." >&2
        exit 1
    fi
    git -C "$source_dir" switch --detach main
fi

if [ -e "$mobile_dir/app/google-services.json" ]; then
    echo "The local main clone requires a checkout without google-services.json; its .main package has no Firebase client configuration." >&2
    exit 1
fi

if [ -z "${JAVA_HOME:-}" ]; then
    if [ ! -x /usr/libexec/java_home ]; then
        echo "Set JAVA_HOME to a JDK 17 installation before building." >&2
        exit 1
    fi
    JAVA_HOME=$(/usr/libexec/java_home -v 17)
    export JAVA_HOME
fi
if [ -z "${ANDROID_HOME:-}" ]; then
    ANDROID_HOME="$HOME/Library/Android/sdk"
    export ANDROID_HOME
fi
if [ ! -d "$ANDROID_HOME" ]; then
    echo "Set ANDROID_HOME to the installed Android SDK directory before building." >&2
    exit 1
fi

"$mobile_dir/gradlew" -p "$mobile_dir" --no-daemon \
    --init-script "$repo_dir/scripts/android-main.init.gradle" \
    "-Poperations.android.main.resources=$repo_dir/scripts/android-main-res" \
    -Poperations.api.debug.base.url=http://10.0.2.2:5214 \
    :app:assembleDebug

python3 - "$mobile_dir/app/build/outputs/apk/debug/output-metadata.json" "$repo_dir/artifacts/android-apps" <<'PY'
import json
from pathlib import Path
import shutil
import sys

metadata_path = Path(sys.argv[1])
element, = json.loads(metadata_path.read_text())["elements"]
output_dir = Path(sys.argv[2])
output_dir.mkdir(parents=True, exist_ok=True)
target = output_dir / f"Operations-Main-Debug-v{element['versionName']}.apk"
shutil.copyfile(metadata_path.parent / element["outputFile"], target)
print(target)
PY

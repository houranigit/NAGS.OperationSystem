#!/bin/sh
set -eu

repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repo_dir"

if [ "$#" -eq 0 ]; then
    set -- up -d --build --wait
fi

# Resolved Compose output contains local passwords and signing keys.
if [ "$1" = "config" ]; then
    if [ "$#" -ne 2 ]; then
        echo "Use config --quiet, --services, --volumes, --networks, or --images." >&2
        exit 1
    fi
    case "$2" in
        --quiet|--services|--volumes|--networks|--images) ;;
        *) echo "Resolved Compose configuration is hidden because it contains credentials." >&2; exit 1 ;;
    esac
fi

case "$1" in
    up|build|run|create)
        if [ "$(git branch --show-current)" = "main" ]; then
            MAIN_DOCKER_BUILD_CONTEXT="$repo_dir"
        else
            source_dir="$repo_dir/artifacts/docker-main-source"
            if [ ! -e "$source_dir/.git" ]; then
                git worktree add --detach "$source_dir" main
            else
                if [ -n "$(git -C "$source_dir" status --porcelain)" ]; then
                    echo "The isolated main checkout has local changes; preserve them before rebuilding." >&2
                    exit 1
                fi
                git -C "$source_dir" switch --detach main
            fi
            MAIN_DOCKER_BUILD_CONTEXT="$source_dir"
        fi
        MAIN_DOCKER_REVISION=$(git -C "$MAIN_DOCKER_BUILD_CONTEXT" rev-parse HEAD)
        export MAIN_DOCKER_BUILD_CONTEXT MAIN_DOCKER_REVISION
        ;;
esac

exec docker compose \
    --project-name operationssystem-main \
    --env-file .env.main \
    -f docker-compose.main.yml \
    "$@"

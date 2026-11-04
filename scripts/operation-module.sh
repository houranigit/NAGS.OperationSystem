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
        branch=$(git branch --show-current)
        if [ "$branch" != "codex/Operation-Module-ImplementationI" ]; then
            echo "Build/start this environment from codex/Operation-Module-ImplementationI (current: $branch)." >&2
            exit 1
        fi
        ;;
esac

exec docker compose \
    --project-name operationssystem-operation-module \
    --env-file .env.operation-module \
    -f docker-compose.operation-module.yml \
    "$@"

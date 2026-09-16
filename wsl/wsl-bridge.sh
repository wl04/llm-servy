#!/usr/bin/env bash
# Invoked with bash -li to load the same Node/nvm PATH as the user's terminal.
set -e
exec python3 "$(dirname -- "${BASH_SOURCE[0]}")/wsl-supervisor.py" "$@"

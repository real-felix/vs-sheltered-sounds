#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
"${DOTNET:-dotnet}" run --project tests/ShelteredSounds.Tests -c Release "$@"
"${DOTNET:-dotnet}" build src/ShelteredSounds -c Release -t:Package --nologo "$@"
python3 scripts/validate_package.py

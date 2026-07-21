#!/bin/sh
set -e

DATA="${ZENITH_DATA:-/data}"
DEFAULT="/app/zenith.yml.default"

mkdir -p "$DATA/worlds"

if [ -d "$DATA/zenith.yml" ]; then
  echo "FATAL: $DATA/zenith.yml is a directory — remove it and mount a file or use a named volume on $DATA" >&2
  exit 1
fi

if [ ! -f "$DATA/zenith.yml" ]; then
  if [ ! -f "$DEFAULT" ]; then
    echo "FATAL: missing default config at $DEFAULT" >&2
    exit 1
  fi
  cp "$DEFAULT" "$DATA/zenith.yml"
  echo "Created default config at $DATA/zenith.yml — edit this file and restart to apply changes."
fi

echo "Zenith data root: $DATA (config: $DATA/zenith.yml)"
exec "$@"

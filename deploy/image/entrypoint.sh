#!/bin/sh
# Product entrypoint (ADR §20 / §68) — seed config, then exec CMD or Wings STARTUP.
set -e

DATA="${ZENITH_DATA:-/data}"
DEFAULT="/opt/zenith/zenith.yml.default"

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

# Pterodactyl Wings: panel injects STARTUP; {{var}} → ${var} then eval.
if [ -n "${STARTUP:-}" ]; then
  MODIFIED_STARTUP=$(eval echo "$(echo "${STARTUP}" | sed -e 's/{{/${/g' -e 's/}}/}/g')")
  echo "Startup: ${MODIFIED_STARTUP}"
  # shellcheck disable=SC2086
  exec /bin/sh -c "${MODIFIED_STARTUP}"
fi

exec "$@"

#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."
image="${1:?Image required}"
work="$(mktemp -d "${TMPDIR:-/tmp}/rentright-sbom.XXXXXX")"
trap 'rm -f "$work/image.tar"; rmdir "$work"' EXIT
mkdir -p artifacts/release
docker save --output "$work/image.tar" "$image"
docker run --rm --mount "type=bind,src=$work,dst=/scan,readonly" \
  aquasec/trivy:0.76.0@sha256:2416d1f74045b57274df1a340eee6b510cacd73c66a6ccb8da092b117eb7d222 \
  image --input /scan/image.tar --format cyclonedx --no-progress \
  > artifacts/release/sbom.cdx.json

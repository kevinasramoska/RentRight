#!/usr/bin/env bash
# Scan an exported application image without credentials or Docker socket access.
set -Eeuo pipefail
cd "$(dirname "$0")/.."
image="${1:?Usage: bash scripts/scan-image.sh IMAGE}"
scan_dir="$(mktemp -d "${TMPDIR:-/tmp}/rentright-scan.XXXXXX")"
trap 'rm -f "$scan_dir/image.tar"; rmdir "$scan_dir"' EXIT
mkdir -p TestResults
docker save --output "$scan_dir/image.tar" "$image"
docker run --rm \
  --mount "type=bind,src=$scan_dir,dst=/scan,readonly" \
  aquasec/trivy:0.76.0@sha256:2416d1f74045b57274df1a340eee6b510cacd73c66a6ccb8da092b117eb7d222 \
  image --input /scan/image.tar --scanners vuln --severity HIGH,CRITICAL \
  --exit-code 1 --no-progress --format json > TestResults/container-vulnerabilities.json
echo 'PASS: no HIGH/CRITICAL application-image vulnerabilities reported.'

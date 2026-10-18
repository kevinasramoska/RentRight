#!/usr/bin/env bash
# Local Compose lifecycle. Never source an environment file or print its secrets.
set -Eeuo pipefail
cd "$(dirname "$0")/.."
repo="$(pwd -P)"
export RENTRIGHT_REVISION="$(git rev-parse HEAD)"
project="${RENTRIGHT_COMPOSE_PROJECT:-rentright-local}"
[[ "$project" =~ ^rentright-[a-z0-9][a-z0-9_-]*$ ]] || { echo 'Project must start with rentright- and use lowercase letters, digits, _ or -.' >&2; exit 1; }
config="${RENTRIGHT_LOCAL_CONFIG_DIR:-$repo/.local/compose/$project}"
[[ "$config" == /* && "$config" != *"'"* && "$config" != *$'\n'* ]] || { echo 'Use an absolute private configuration path without quotes/newlines.' >&2; exit 1; }
env_file="$config/.env"
action="${1:-help}"
require() { command -v "$1" >/dev/null || { echo "Missing prerequisite: $1" >&2; exit 1; }; }
compose() {
  # Keep credentials tied to this private file, not unrelated shell overrides.
  (unset RENTRIGHT_SQL_PASSWORD RENTRIGHT_ADMIN_PASSWORD RENTRIGHT_ADMIN_EMAIL RENTRIGHT_CERT_DIR
   docker compose --project-name "$project" --env-file "$env_file" --file "$repo/compose.yml" "$@")
}
require docker
case "$action" in
  init)
    require dotnet; require openssl
    [[ ! -e "$env_file" ]] || { echo "Configuration already exists at $config; nothing was overwritten."; exit 1; }
    umask 077
    mkdir -p "$config/certs"
    chmod 700 "$config"
    # Keep exports private while the SDK writes them. A complete pair can survive a retry.
    if [[ ! -f "$config/certs/localhost.pem" || ! -f "$config/certs/localhost.key" ]]; then
      chmod 700 "$config/certs"
      dotnet dev-certs https --export-path "$config/certs/localhost.pem" --format PEM --no-password --quiet
    fi
    openssl x509 -in "$config/certs/localhost.pem" -checkend 0 -noout >/dev/null
    chmod 444 "$config/certs/localhost.pem" "$config/certs/localhost.key"
    # The private parent stays 0700; only this subdirectory is mounted read-only.
    chmod 755 "$config/certs"
    {
      printf 'RENTRIGHT_SQL_PASSWORD=T!%sa9\n' "$(openssl rand -hex 24)"
      printf 'RENTRIGHT_ADMIN_EMAIL=admin@example.test\n'
      printf 'RENTRIGHT_ADMIN_PASSWORD=T!%sa9\n' "$(openssl rand -hex 24)"
      printf "RENTRIGHT_CERT_DIR='%s/certs'\n" "$config"
      printf 'RENTRIGHT_HTTPS_PORT=7443\n'
    } > "$env_file"
    chmod 600 "$env_file"
    echo "Private local settings created at $env_file. Open that file locally for the generated admin login."
    echo 'No certificate was trusted automatically. If needed, run dotnet dev-certs https --trust yourself.'
    ;;
  up|migrate|logs|status|stop|restart|reset)
    [[ -f "$env_file" ]] || { echo 'Missing private local configuration. Run bash scripts/local-dev.sh init first.' >&2; exit 1; }
    docker info >/dev/null
    compose config --quiet
    case "$action" in
      up)
        require curl
        compose up --build --detach --wait --wait-timeout 240
        address="$(compose port app 8443)"
        port="${address##*:}"
        [[ "$port" =~ ^[0-9]+$ && "$port" != 0 ]] || { echo 'Compose did not publish an HTTPS port.' >&2; exit 1; }
        curl --silent --show-error --fail --max-time 10 --cacert "$config/certs/localhost.pem" \
          --resolve "localhost:$port:127.0.0.1" "https://localhost:$port/api/v1/session" >/dev/null
        echo "RentRight is ready at https://localhost:${address##*:}/app/properties"
        ;;
      migrate)
        compose build initialize
        compose up --detach --wait --wait-timeout 180 sql
        compose run --rm --no-deps initialize
        ;;
      logs) compose logs --tail 100 --follow app initialize sql ;;
      status) compose ps --all ;;
      stop) compose down --timeout 30 ;;
      restart) compose restart app; compose up --detach --no-deps --wait --wait-timeout 120 app ;;
      reset)
        [[ "${2:-}" == --confirm && "${3:-}" == "$project" && "$#" == 3 ]] || {
          echo "Refusing reset. This deletes only $project SQL data and authentication-key volumes. Use reset --confirm $project to confirm." >&2; exit 1;
        }
        for volume in sql-data data-protection; do
          name="${project}_${volume}"
          if docker volume inspect "$name" >/dev/null 2>&1; then
            owner="$(docker volume inspect "$name" --format '{{index .Labels "com.docker.compose.project"}}')"
            kind="$(docker volume inspect "$name" --format '{{index .Labels "com.docker.compose.volume"}}')"
            [[ "$owner" == "$project" && "$kind" == "$volume" ]] || { echo 'Volume ownership mismatch; refusing reset.' >&2; exit 1; }
          fi
        done
        compose down --volumes --timeout 30
        echo "Removed only $project containers/network and its SQL/authentication-key volumes. Data is not recoverable without a backup; private local settings were kept."
        ;;
    esac
    ;;
  *) echo 'Usage: bash scripts/local-dev.sh {init|up|migrate|logs|status|stop|restart|reset --confirm rentright-local}'; [[ "$action" == help ]] ;;
esac

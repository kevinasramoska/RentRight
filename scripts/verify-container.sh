#!/usr/bin/env bash
# Disposable image smoke only: never accepts an external database or reuses a volume.
set -Eeuo pipefail
cd "$(dirname "$0")/.."
image="${1:-rentright:p10-01}"
for dependency in docker dotnet curl openssl; do
  command -v "$dependency" >/dev/null || { echo "Missing prerequisite: $dependency" >&2; exit 1; }
done
docker info >/dev/null
image="$(docker image inspect "$image" --format '{{.Id}}')"
work="$(mktemp -d -t rentright-container.XXXXXXXX)"
suffix="$(openssl rand -hex 8)"
network="rentright-smoke-$suffix"
sql="rentright-smoke-sql-$suffix"
app="rentright-smoke-app-$suffix"
cleanup() {
  docker rm -f "$app" "$sql" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  if [[ "$work" == */rentright-container.* && -d "$work" ]]; then rm -r -- "$work"; fi
}
trap cleanup EXIT
trap 'echo "Container smoke failed at line $LINENO; disposable resources will be removed." >&2' ERR
trap 'exit 130' INT
trap 'exit 143' TERM

echo "Checking runtime identity and image contents."
docker run --rm --entrypoint sh "$image" -ec '
  test "$(id -u)" = 1654
  test -s /app/wwwroot/app/index.html
  test -f /app/RentRight.dll
  test ! -d /usr/share/dotnet/sdk
  for tool in node npm git; do ! command -v "$tool" >/dev/null; done
  test -z "$(find /app \( -name .git -o -name node_modules -o -name ClientApp -o -name RentRight.Tests -o -name secrets.json -o -name .env -o -name "*.pfx" -o -name "*.pem" -o -name "*.key" -o -name appsettings.Development.json \) -print)"
'
[[ "$(docker image inspect "$image" --format '{{.Config.User}}')" == 1654 ]]
[[ "$(docker image inspect "$image" --format '{{json .Config.ExposedPorts}}')" == '{"8080/tcp":{}}' ]]

docker network create "$network" >/dev/null
export MSSQL_SA_PASSWORD="T!$(openssl rand -hex 24)a9"
# SQL Server's Linux image is amd64; emulation must be enabled on Apple Silicon.
docker run -d --name "$sql" --platform linux/amd64 --network "$network" --publish 127.0.0.1::1433 \
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD \
  mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74 >/dev/null
sqlcmd() {
  docker exec "$sql" sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -b -l 2 -t 10 "$@"' sh "$@"
}
ready=0
for ((attempt=0; attempt<60; attempt++)); do
  if sqlcmd -Q 'SELECT 1' >/dev/null 2>&1; then ready=1; break; fi
  sleep 2
done
[[ "$ready" == 1 ]] || { echo 'Disposable SQL Server did not become ready.' >&2; exit 1; }
sqlcmd -Q 'CREATE DATABASE [RentRightImageSmoke]' >/dev/null
sql_port="$(docker port "$sql" 1433/tcp)"
export ConnectionStrings__DefaultConnection="Server=${sql_port/:/,};Database=RentRightImageSmoke;User ID=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
echo "Applying EF migrations only to the SQL container created by this smoke."
env Logging__LogLevel__Microsoft=Warning dotnet ef database update --project RentRight.csproj --configuration Release
echo "Disposable SQL database migrated. Seeding one fictional listing."
# Synthetic public listing only. No password or usable login is seeded.
sqlcmd -d RentRightImageSmoke -Q "INSERT INTO AspNetUsers (Id, FullName, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES ('image-smoke-owner', 'Fictional owner', 0, 0, 0, 0, 0); INSERT INTO Properties (Title, Address, RentAmount, Description, LandlordId) VALUES ('Container smoke home', 'Synthetic street', 1200, 'Disposable verification listing', 'image-smoke-owner');" >/dev/null

export ConnectionStrings__DefaultConnection="Server=$sql,1433;Database=RentRightImageSmoke;User ID=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
echo "Starting read-only application container."
docker run -d --name "$app" --network "$network" --read-only --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --tmpfs /home/app/.aspnet:rw,noexec,nosuid,size=8m,uid=1654,gid=1654 \
  --publish 127.0.0.1::8080 --env ConnectionStrings__DefaultConnection \
  --env Geocoding__Enabled=false "$image" >/dev/null
base="http://$(docker port "$app" 8080/tcp)"
ready=0
for ((attempt=0; attempt<60; attempt++)); do
  if curl --silent --fail --max-time 3 "$base/api/v1/session" > "$work/session.json"; then ready=1; break; fi
  sleep 2
done
[[ "$ready" == 1 ]] || { echo 'Application did not become ready; inspect startup configuration.' >&2; exit 1; }
echo "Checking HTTP pages and API responses at $base."
grep -Fq '"isAuthenticated":false' "$work/session.json"
curl --silent --show-error --fail "$base/api/v1/properties" > "$work/properties.json"
grep -Fq 'Container smoke home' "$work/properties.json"
curl --silent --show-error --fail "$base/Home/Index" > "$work/mvc.html"
grep -Fq 'Container smoke home' "$work/mvc.html"
curl --silent --show-error --fail "$base/app/properties" > "$work/app.html"
asset="$(sed -n 's/.*src="\([^"]*\.js\)".*/\1/p' "$work/app.html")"
[[ "$asset" == /app/assets/*.js ]]
curl --silent --show-error --fail "$base$asset" >/dev/null
[[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base/api/v1/workspace")" == 401 ]]
[[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base/api/v1/not-a-route")" == 404 ]]
[[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base/app/missing.js")" == 404 ]]
[[ "$(curl --silent --output /dev/null --write-out '%{redirect_url}' "$base/")" == "$base/app/properties" ]]
echo "PASS: non-root, read-only runtime; React/JS, MVC and SQL-backed API; private/API/static route boundaries."
echo "Temporary browser URL: $base/app/properties (removed when this script exits)."
if [[ "${RENTRIGHT_SMOKE_PAUSE:-0}" == 1 ]]; then
  echo 'Keeping the disposable preview available for 60 seconds, then cleaning up.'
  sleep 60
fi

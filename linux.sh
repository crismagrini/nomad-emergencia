#!/usr/bin/env bash
set -euo pipefail
mode=$1
root=/opt/project-nomad

if [ "$mode" = setup ]; then
  if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y docker.io docker-compose-v2 curl
  fi
  umask 077
  install -d -m 700 "$root"
  install -d -m 755 "$root/storage/logs" "$root/storage/zim"
  touch "$root/storage/logs/admin.log"
  if [ ! -f "$root/compose.yml" ]; then
    app=$(openssl rand -hex 32)
    db=$(openssl rand -hex 24)
    admin=$(openssl rand -hex 24)
    sed -e "s|APP_KEY=replaceme|APP_KEY=$app|" \
        -e "s|DB_PASSWORD=replaceme|DB_PASSWORD=$db|" \
        -e "s|MYSQL_PASSWORD=replaceme|MYSQL_PASSWORD=$db|" \
        -e "s|MYSQL_ROOT_PASSWORD=replaceme|MYSQL_ROOT_PASSWORD=$admin|" \
        -e 's|URL=replaceme|URL=http://localhost:8080|' \
        -e 's|"8080:8080"|"127.0.0.1:8080:8080"|' \
        -e 's|"9999:8080"|"127.0.0.1:9999:8080"|' \
        -e 's/pull_policy: always/pull_policy: missing/' "$2" > "$root/compose.yml.tmp"
    docker compose -f "$root/compose.yml.tmp" config --quiet
    mv "$root/compose.yml.tmp" "$root/compose.yml"
  fi
  exit 0
fi

cd "$root"
if [ "$mode" = stop ]; then
  docker ps -q | xargs -r docker stop
  exit 0
fi

mount --make-rshared /
source=$2
if ! mountpoint -q "$root/storage/zim"; then
  mount --bind "$source" "$root/storage/zim"
elif [ ! "$source" -ef "$root/storage/zim" ]; then
  echo 'A biblioteca está montada a partir de outra pasta.' >&2
  exit 1
fi
systemctl start docker
if [ "$mode" = online ]; then
  docker compose --progress quiet up -d --wait --wait-timeout 240
else
  docker compose up -d --pull never --wait --wait-timeout 240
fi
docker ps -aq --filter 'name=^/nomad_' --filter status=exited | xargs -r docker start

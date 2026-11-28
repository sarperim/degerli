#!/usr/bin/env bash
#
# One-line idempotent local development stack launcher (SLICE-041).
# Starts PostgreSQL, MailPit, API (via compose.dev.yml) and the containerized
# Vite dev server (`degerli-dev-web`) without requiring Node.js on the host.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

COMPOSE_FILE="${COMPOSE_FILE:-${REPO_ROOT}/compose.dev.yml}"
WEB_CONTAINER="${WEB_CONTAINER:-degerli-dev-web}"
WEB_IMAGE="${WEB_IMAGE:-node:22-slim}"
WEB_VOLUME="${WEB_VOLUME:-degerli-web-node-modules}"
WEB_PORT="${WEB_PORT:-5173}"
API_PORT="${API_PORT:-8080}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-120}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Start and check the Değerli local development stack (Postgres, MailPit, API, Web SPA).

Options:
  (default)     Ensure backend compose services and ${WEB_CONTAINER} are running and healthy
  --migrate     Start postgres, run EF migrations (--profile tools run --rm migrate), then start stack
  --no-wait     Start containers without waiting for HTTP readiness
  --down        Stop and remove ${WEB_CONTAINER} (if present) and run docker compose down
  --status      Show compose services, ${WEB_CONTAINER} status, and HTTP endpoint checks
  -h, --help    Show this help message and exit

Environment overrides:
  COMPOSE_FILE  Path to compose file (default: \${REPO_ROOT}/compose.dev.yml)
  WEB_CONTAINER Web container name (default: degerli-dev-web)
  WEB_IMAGE     Node image for Vite dev server (default: node:22-slim)
  WEB_VOLUME    Docker volume for node_modules (default: degerli-web-node-modules)
  WEB_PORT      Vite dev server port (default: 5173)
  API_PORT      API port (default: 8080)
  WAIT_TIMEOUT  Health check timeout in seconds (default: 120)
EOF
}

MODE="up"
RUN_MIGRATE=0
NO_WAIT=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --migrate)
      RUN_MIGRATE=1
      shift
      ;;
    --no-wait)
      NO_WAIT=1
      shift
      ;;
    --down)
      MODE="down"
      shift
      ;;
    --status)
      MODE="status"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Error: unknown option '$1'" >&2
      usage >&2
      exit 2
      ;;
  esac
done

compose() {
  docker compose -f "$COMPOSE_FILE" "$@"
}

http_status_code() {
  local url="$1"
  local code
  code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$url" 2>/dev/null || true)"
  printf '%s' "${code:-000}"
}

is_http_ready() {
  local code
  code="$(http_status_code "$1")"
  [[ "$code" =~ ^[23][0-9][0-9]$ ]]
}

print_urls() {
  cat <<EOF
Local URLs:
  Web UI:  http://127.0.0.1:${WEB_PORT}
  API:     http://127.0.0.1:${API_PORT}
  MailPit: http://127.0.0.1:8025
EOF
}

cmd_status() {
  echo "=== Docker Compose Services ==="
  compose ps

  echo ""
  echo "=== Web Container (${WEB_CONTAINER}) ==="
  if docker container inspect "$WEB_CONTAINER" >/dev/null 2>&1; then
    docker inspect -f 'Status: {{.State.Status}} (running={{.State.Running}})' "$WEB_CONTAINER"
  else
    echo "Status: not created"
  fi

  echo ""
  echo "=== HTTP Endpoints ==="
  local api_url="http://127.0.0.1:${API_PORT}/health"
  local web_url="http://127.0.0.1:${WEB_PORT}/"
  local api_code web_code
  api_code="$(http_status_code "$api_url")"
  web_code="$(http_status_code "$web_url")"

  if [[ "$api_code" =~ ^[23][0-9][0-9]$ ]]; then
    echo "API (${api_url}): OK (HTTP ${api_code})"
  else
    echo "API (${api_url}): DOWN (HTTP ${api_code})"
  fi

  if [[ "$web_code" =~ ^[23][0-9][0-9]$ ]]; then
    echo "Web (${web_url}): OK (HTTP ${web_code})"
  else
    echo "Web (${web_url}): DOWN (HTTP ${web_code})"
  fi
}

cmd_down() {
  if docker container inspect "$WEB_CONTAINER" >/dev/null 2>&1; then
    echo "Stopping and removing ${WEB_CONTAINER}..."
    docker rm -f "$WEB_CONTAINER"
  else
    echo "${WEB_CONTAINER} does not exist; skipping removal."
  fi

  echo "Stopping compose stack..."
  compose down
}

ensure_web_container() {
  if docker container inspect "$WEB_CONTAINER" >/dev/null 2>&1; then
    local running
    running="$(docker inspect -f '{{.State.Running}}' "$WEB_CONTAINER")"
    if [[ "$running" == "true" ]]; then
      echo "${WEB_CONTAINER} is already running."
    else
      echo "Starting existing ${WEB_CONTAINER} container..."
      docker start "$WEB_CONTAINER" >/dev/null
    fi
    return 0
  fi

  if ! docker run --rm \
    -v "${WEB_VOLUME}:/app/node_modules" \
    "$WEB_IMAGE" \
    test -e /app/node_modules/.bin/vite >/dev/null 2>&1; then
    echo "Installing web dependencies into ${WEB_VOLUME} (npm ci)..."
    docker run --rm \
      -v "${REPO_ROOT}/src/Web:/app" \
      -v "${WEB_VOLUME}:/app/node_modules" \
      -w /app \
      "$WEB_IMAGE" \
      npm ci
  fi

  echo "Creating and starting ${WEB_CONTAINER}..."
  docker run -d \
    --name "$WEB_CONTAINER" \
    --restart unless-stopped \
    --network host \
    -w /app \
    -e "VITE_API_PROXY_TARGET=http://127.0.0.1:${API_PORT}" \
    -v "${REPO_ROOT}/src/Web:/app" \
    -v "${WEB_VOLUME}:/app/node_modules" \
    "$WEB_IMAGE" \
    npm run dev -- --host 127.0.0.1 --port "$WEB_PORT" >/dev/null
}

wait_for_health() {
  local api_url="http://127.0.0.1:${API_PORT}/health"
  local web_url="http://127.0.0.1:${WEB_PORT}/"
  local deadline=$((SECONDS + WAIT_TIMEOUT))

  echo "Waiting for API and Web to become ready (timeout: ${WAIT_TIMEOUT}s)..."
  while true; do
    if is_http_ready "$api_url" && is_http_ready "$web_url"; then
      return 0
    fi
    if (( SECONDS >= deadline )); then
      echo "Error: timed out after ${WAIT_TIMEOUT}s waiting for HTTP readiness." >&2
      echo "  API (${api_url}): HTTP $(http_status_code "$api_url")" >&2
      echo "  Web (${web_url}): HTTP $(http_status_code "$web_url")" >&2
      return 1
    fi
    sleep 1
  done
}

cmd_up() {
  if (( RUN_MIGRATE == 1 )); then
    echo "Starting postgres and running EF migrations..."
    compose up -d postgres
    compose --profile tools run --rm migrate
  fi

  echo "Ensuring backend stack is up..."
  compose up -d

  ensure_web_container

  if (( NO_WAIT == 0 )); then
    wait_for_health
  else
    echo "Skipping HTTP readiness wait (--no-wait)."
  fi

  echo ""
  print_urls
}

case "$MODE" in
  status)
    cmd_status
    ;;
  down)
    cmd_down
    ;;
  up)
    cmd_up
    ;;
esac

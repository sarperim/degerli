# Production web image: Caddy 2 + the built React SPA (architecture §5 reverse-proxy
# row, §10.5 security posture; TKT-int-006).
#
# Two stages: build the SPA with Vite, then bake it into the stock Caddy image so the
# reverse proxy and the static assets ship as one immutable artifact. Pushed to
#   ghcr.io/sarperim/degerli/web:<sha> and :latest
# by .github/workflows/deploy.yml. It carries NO secrets — the site address comes from
# the Caddyfile's {$DOMAIN} environment placeholder at runtime (architecture §6.3).

# --- SPA build -----------------------------------------------------------------
FROM node:22-bookworm-slim AS spa

WORKDIR /web

# Lockfile first so the dependency layer caches across source-only changes.
COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci

COPY src/Web/ ./
RUN npm run build

# --- Caddy runtime -------------------------------------------------------------
# Runs as root (the stock image default): binding the privileged ports :80/:443 and
# reading the Let's Encrypt material in the caddy-data volume needs it. The process
# serves only static SPA files and a reverse proxy to `api`; no untrusted code runs here.
FROM caddy:2

# Built SPA → the document root referenced by the Caddyfile.
COPY --from=spa /web/dist /srv

# Site config. The stock caddy image's default entrypoint runs
# `caddy run --config /etc/caddy/Caddyfile --adapter caddyfile`, so no override is
# required in compose.prod.yml.
COPY deploy/caddy/Caddyfile /etc/caddy/Caddyfile

EXPOSE 80
EXPOSE 443

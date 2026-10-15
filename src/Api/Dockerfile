# Production API image (architecture 01-system-architecture.md §6.2, TKT-int-006).
#
# Multi-stage: build/publish the ASP.NET Core API on the .NET 10 SDK, then run it on
# the lean ASP.NET Core runtime. The image is pushed to
#   ghcr.io/sarperim/degerli/api:<sha> and :latest
# by .github/workflows/deploy.yml. It carries NO secrets (runtime env only) and the
# framework-dependent publish keeps it small (architecture §6.3).
#
# The deploy job runs `docker compose run --rm api migrate` before `up -d`; that
# command reaches the `migrate` mode implemented in src/Api/Program.cs.
#
# This is deliberately NOT Dockerfile.dev (hot-reload local development); see
# compose.prod.yml for the production stack.

# --- build ---------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Directory.Build.props carries TargetFramework/nullability for every backend
# project, so it must be present before the first restore.
COPY Degerli.sln Directory.Build.props ./
COPY src/ ./src/

RUN dotnet publish src/Api/Degerli.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- runtime -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# curl backs the container healthcheck (and lets the deploy smoke-check reuse the
# same probe spirit as ci.yml).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Ownership to the image's non-root `app` user so Serilog's rolling file sink
# (appsettings.json `logs/`) can write without running the container as root.
COPY --from=build --chown=app:app /app/publish ./

# `WORKDIR /app` created /app as root:root; `COPY --chown` only re-owns the copied
# contents, not the pre-existing directory. As `USER app` the Serilog file sink
# cannot create /app/logs → UnauthorizedAccessException at startup, so hand the whole
# app dir (and the log dir) to `app` before dropping privileges.
RUN mkdir -p /app/logs && chown -R app:app /app

USER app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

EXPOSE 8080

# Liveness + DB check (`Program.cs` MapHealthChecks("/health"); architecture §10.3).
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Degerli.Api.dll"]

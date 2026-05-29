# syntax=docker/dockerfile:1
# DirectPlayForce — Jellyfin Plugin Build
# Everything runs inside Docker; no local .NET SDK required.
#
# Build:
#   docker build --output type=local,dest=./dist .
# Or via:
#   ./build.sh
#
# Output: dist/Jellyfin.Plugin.DirectPlayForce.zip

# ─── Stage 1: Compile ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy project file first for better layer caching on restore
COPY Jellyfin.Plugin.DirectPlayForce.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish \
        -c Release \
        --no-restore \
        --no-self-contained \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o /out

# ─── Stage 2: Package ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS package

RUN apt-get update -qq && apt-get install -y -qq zip

COPY --from=build /out/Jellyfin.Plugin.DirectPlayForce.dll /plugin/
COPY meta.json /plugin/

RUN mkdir -p /dist && \
    cd /plugin && \
    zip /dist/Jellyfin.Plugin.DirectPlayForce.zip \
        Jellyfin.Plugin.DirectPlayForce.dll \
        meta.json && \
    echo "Plugin packaged:" && \
    ls -lh /dist/

# ─── Stage 3: Export ──────────────────────────────────────────────────────────
FROM scratch
COPY --from=package /dist/ /

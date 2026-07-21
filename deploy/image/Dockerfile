# Zenith product image (ADR §20 / §68) — one image for Compose, Dokploy, Pterodactyl.
#
# Layout:
#   /opt/zenith/          — published DLL + zenith.yml.default (read-only)
#   $ZENITH_DATA          — config, worlds, server.guid (default /data)
#
# No fixed USER — Compose/Dokploy typically root; Wings may run as uid 1000 (container).
# Build from repo root:
#   docker build -f deploy/image/Dockerfile -t ghcr.io/zenith-bedrock/zenith:latest .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/zenith/zenith.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN groupadd --system --gid 1000 container \
    && useradd --system --uid 1000 --gid container --home-dir /home/container --create-home container \
    && mkdir -p /data/worlds /opt/zenith /home/container

COPY --from=build /out /opt/zenith
COPY deploy/zenith.yml /opt/zenith/zenith.yml.default
COPY deploy/image/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENV ZENITH_DATA=/data
EXPOSE 19132/udp
VOLUME ["/data"]
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "/opt/zenith/zenith.dll"]

# Thin root alias for Dokploy / `docker build .` — keep identical to deploy/image/Dockerfile (ADR §20).
# Prefer: docker build -f deploy/image/Dockerfile -t ghcr.io/zenith-bedrock/zenith:latest .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/zenith/zenith.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN mkdir -p /data/worlds /opt/zenith /home/container \
    && (getent group container >/dev/null || groupadd --system container) \
    && (id -u container >/dev/null 2>&1 \
        || useradd --system --gid container --home-dir /home/container --create-home container)

COPY --from=build /out /opt/zenith
COPY deploy/zenith.yml /opt/zenith/zenith.yml.default
COPY deploy/image/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENV ZENITH_DATA=/data
EXPOSE 19132/udp
VOLUME ["/data"]
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "/opt/zenith/zenith.dll"]

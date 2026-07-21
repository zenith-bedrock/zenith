# Multi-stage publish of Zenith (ADR §20).
# Runtime layout:
#   /app/          — read-only published DLL + embedded assets
#   /data/         — persistent config + worlds (ZENITH_DATA; PocketMine/Endstone-style)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/zenith/zenith.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY deploy/zenith.yml /app/zenith.yml.default
COPY deploy/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh \
    && mkdir -p /data/worlds

ENV ZENITH_DATA=/data
EXPOSE 19132/udp
VOLUME ["/data"]
ENTRYPOINT ["/docker-entrypoint.sh"]
CMD ["dotnet", "/app/zenith.dll"]

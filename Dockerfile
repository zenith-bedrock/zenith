# Multi-stage publish of Zenith (ADR §20). WORKDIR must stay /app so zenith.yml
# resolves next to the DLL via AppContext.BaseDirectory.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/zenith/zenith.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 19132/udp
ENTRYPOINT ["dotnet", "/app/zenith.dll"]

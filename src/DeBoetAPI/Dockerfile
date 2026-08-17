# Build context is the repository root (see compose.yaml), so paths below are repo-relative.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files alone so the layer caches across source-only changes.
COPY src/DeBoetAPI/DeBoetAPI.csproj src/DeBoetAPI/
RUN dotnet restore src/DeBoetAPI/DeBoetAPI.csproj

COPY src/ src/
RUN dotnet publish src/DeBoetAPI/DeBoetAPI.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

# The chiseled image ships a non-root `app` user (uid 1654) and no shell.
USER $APP_UID

# /app is root-owned, so the app user could not create the log directory at runtime. There is
# no shell here to mkdir/chown with, but WORKDIR creates a missing directory owned by the
# current USER — which is exactly what a mounted volume needs to inherit.
WORKDIR /app/logs
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DeBoetAPI.dll"]

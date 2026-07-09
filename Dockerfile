# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/SpatialAI.Api/SpatialAI.Api.csproj
RUN dotnet publish src/SpatialAI.Api/SpatialAI.Api.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Public conference defaults. SQLite DBs, saved spaces, and the data-protection key ring all live on
# the mounted /data volume so they survive restarts. Override any of these via `docker run -e` / compose.
ENV Catalog__Database=/data/catalog.db \
    Auth__Database=/data/app.db \
    Spaces__Directory=/data/spaces \
    DataProtection__KeysDirectory=/data/keys \
    LLM__Model=claude-haiku-4-5 \
    PublicMode=true \
    Auth__Required=true \
    Auth__RequireVerification=true \
    Budget__MessagesPerUser=50 \
    Budget__GlobalMessageCeiling=1500

VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "SpatialAI.Api.dll"]

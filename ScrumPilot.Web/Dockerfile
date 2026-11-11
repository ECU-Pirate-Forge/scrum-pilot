# Artifact-only target. Aspire's Blazor gateway publishes and serves this site,
# including runtime public configuration, telemetry proxy and SPA fallback.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ScrumPilot.Web/ScrumPilot.Web.csproj
RUN dotnet publish ScrumPilot.Web/ScrumPilot.Web.csproj --no-restore -c Release -o /app/publish

FROM scratch AS artifacts
COPY --from=build /app/publish /app/publish

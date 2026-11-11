# Artifact-only target; the AppHost's supported Blazor gateway is the runtime host.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ScrumPilot.FluentWeb/ScrumPilot.FluentWeb.csproj
RUN dotnet publish ScrumPilot.FluentWeb/ScrumPilot.FluentWeb.csproj --no-restore -c Release -o /app/publish

FROM scratch AS artifacts
COPY --from=build /app/publish /app/publish

# See https://docs.docker.com/engine/reference/builder/ for details on Dockerfile syntax

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ScrumPilot.API/ScrumPilot.API.csproj
RUN dotnet publish ScrumPilot.API/ScrumPilot.API.csproj --no-restore -c Release -o /app/publish

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ScrumPilot.API.dll"]

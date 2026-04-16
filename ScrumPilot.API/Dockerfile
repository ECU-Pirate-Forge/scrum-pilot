# See https://docs.docker.com/engine/reference/builder/ for details on Dockerfile syntax

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.201 AS build
WORKDIR /src
COPY ["ScrumPilot.API/ScrumPilot.API.csproj", "ScrumPilot.API/"]
RUN dotnet restore "ScrumPilot.API/ScrumPilot.API.csproj"
COPY . .
WORKDIR "/src/ScrumPilot.API"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ScrumPilot.API.dll"]

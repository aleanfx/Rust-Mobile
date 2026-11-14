# The Oxfall game server as a container, for any host (Render, Koyeb, Fly, a VPS).
# Build from the repository root:  docker build -f server/Dockerfile -t oxfall-server .
# Run:  docker run -p 7777:7777 -e OXFALL_SEED=20261113 oxfall-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Assets/Oxfall/Scripts/Core Assets/Oxfall/Scripts/Core
COPY server/Oxfall.Server server/Oxfall.Server
RUN dotnet publish server/Oxfall.Server -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV PORT=7777
EXPOSE 7777
ENTRYPOINT ["dotnet", "Oxfall.Server.dll"]

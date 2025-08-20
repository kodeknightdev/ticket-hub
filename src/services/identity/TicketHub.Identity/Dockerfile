# Build context is the repository root (shared projects and protos live outside this folder).
FROM mcr.microsoft.com/dotnet/sdk:9.0.200 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/
COPY src/services/identity/TicketHub.Identity/ src/services/identity/TicketHub.Identity/
RUN dotnet restore src/services/identity/TicketHub.Identity/TicketHub.Identity.csproj
RUN dotnet publish src/services/identity/TicketHub.Identity/TicketHub.Identity.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.2 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=5 CMD curl -fsS http://localhost:8080/alive || exit 1
ENTRYPOINT ["dotnet", "TicketHub.Identity.dll"]

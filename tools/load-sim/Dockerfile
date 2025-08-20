FROM mcr.microsoft.com/dotnet/sdk:9.0.200 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/TicketHub.Contracts/ shared/TicketHub.Contracts/
COPY tools/load-sim/TicketHub.LoadSim/ tools/load-sim/TicketHub.LoadSim/
RUN dotnet publish tools/load-sim/TicketHub.LoadSim/TicketHub.LoadSim.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0.2
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "TicketHub.LoadSim.dll"]

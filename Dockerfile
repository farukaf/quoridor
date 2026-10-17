FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build
WORKDIR /src
COPY global.json ./
COPY src/Quoridor/ src/Quoridor/
RUN dotnet restore src/Quoridor/Quoridor.csproj --locked-mode
RUN dotnet publish src/Quoridor/Quoridor.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Quoridor.dll"]

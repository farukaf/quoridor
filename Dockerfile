FROM mcr.microsoft.com/dotnet/sdk:11.0.100-alpine3.24@sha256:52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290 AS build
WORKDIR /src
COPY global.json ./
COPY src/Quoridor/ src/Quoridor/
RUN dotnet restore src/Quoridor/Quoridor.csproj --locked-mode
RUN dotnet publish src/Quoridor/Quoridor.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-alpine3.24@sha256:486297ae006e95ee2ee3112a2c03e3d26e41fbbc64b7a29e28e7a18cc36b12bb AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 DataDirectory=/data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1
COPY --from=build /app/publish .
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data
VOLUME /data
USER $APP_UID
ENTRYPOINT ["dotnet", "Quoridor.dll"]

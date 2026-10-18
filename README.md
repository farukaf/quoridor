# Quoridor

Play Quoridor online with friends. Start a room, share its link, and race to the opposite side of the board while placing walls to block your opponent.

Built with [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) on ASP.NET Core. Game and player history use SQLite by default, with PostgreSQL available through application configuration.

## Run with Docker

```sh
docker run --rm -p 8080:8080 farukaf/quoridor:latest
```

Open [http://localhost:8080](http://localhost:8080) and create a room to invite another player. Browse the published [Docker Hub image and tags](https://hub.docker.com/r/farukaf/quoridor).

## Source and support

- [Source code and issues](https://github.com/farukaf/quoridor)
- [Docker Hub image and tags](https://hub.docker.com/r/farukaf/quoridor/tags)
   

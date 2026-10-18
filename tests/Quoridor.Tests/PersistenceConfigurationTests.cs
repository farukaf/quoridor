using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Quoridor.Services;

namespace Quoridor.Tests;

public sealed class PersistenceConfigurationTests
{
    [Theory]
    [InlineData("Unknown", "Data Source=quoridor.db", "")]
    [InlineData("SQLite", "", "")]
    [InlineData("SQLite", "Data Source=:memory:", "")]
    [InlineData("PostgreSql", "Data Source=quoridor.db", "")]
    [InlineData("PostgreSql", "Data Source=quoridor.db", "Database=quoridor")]
    public void InvalidConfigurationPreventsApplicationStartup(string provider,string sqlite,string postgres)
    {
        using var database=new TestDatabase("SQLite");
        database.Values["Persistence:Provider"]=provider;
        database.Values["ConnectionStrings:SQLite"]=sqlite;
        database.Values["ConnectionStrings:PostgreSql"]=postgres;
        using var host=new InvalidHost(database.Values);
        Assert.Throws<InvalidOperationException>(()=>host.CreateClient());
        Assert.False(File.Exists(Path.Combine(database.DirectoryPath,"quoridor.db")));
    }
    [Fact]
    public void PostgreSqlConnectionFailureNeverFallsBackToSQLite()
    {
        using var database=new TestDatabase("SQLite");
        database.Values["Persistence:Provider"]="PostgreSql";
        database.Values["ConnectionStrings:PostgreSql"]="Host=127.0.0.1;Port=1;Database=unreachable;Username=unused;Timeout=1";
        Assert.ThrowsAny<Npgsql.NpgsqlException>(()=>new GameStore(database.Configuration()));
        Assert.False(File.Exists(Path.Combine(database.DirectoryPath,"quoridor.db")));
    }
    [Fact]
    public void ExistingSQLiteFileIsReopenedWithoutMovingOrMigratingData()
    {
        using var database=new TestDatabase("SQLite");
        Directory.CreateDirectory(database.DirectoryPath);
        var winner=Guid.NewGuid(); var opponent=Guid.NewGuid(); var match=Guid.NewGuid();
        using(var connection=database.Open())
        using(var command=connection.CreateCommand())
        {
            // Schema emitted by the original SQLite-only leaderboard implementation.
            command.CommandText="""
                CREATE TABLE Players(Id TEXT PRIMARY KEY,Name TEXT NOT NULL);
                CREATE TABLE Matches(MatchId TEXT PRIMARY KEY,RoomId TEXT NOT NULL,WinnerId TEXT NOT NULL,WinnerName TEXT NOT NULL,OpponentId TEXT NOT NULL,OpponentName TEXT NOT NULL,FinishedAt TEXT NOT NULL,CHECK(WinnerId<>OpponentId));
                INSERT INTO Players VALUES(@winner,'Legacy winner'),(@opponent,'Legacy opponent');
                INSERT INTO Matches VALUES(@match,@room,@winner,'Legacy winner',@opponent,'Legacy opponent','2026-10-17T12:00:00.0000000+00:00');
                """;
            foreach(var value in new[]{("@winner",winner),("@opponent",opponent),("@match",match),("@room",Guid.NewGuid())})
            { var parameter=command.CreateParameter(); parameter.ParameterName=value.Item1; parameter.Value=value.Item2.ToString(); command.Parameters.Add(parameter); }
            command.ExecuteNonQuery();
        }
        using var store=new GameStore(database.Configuration());
        Assert.Equal(match,Assert.Single(store.History(winner)).MatchId);
        Assert.Equal(1L,Assert.Single(store.Leaderboard(),p=>p.PlayerId==winner).Wins);
        Assert.True(File.Exists(Path.Combine(database.DirectoryPath,"quoridor.db")));
    }

    private sealed class InvalidHost(Dictionary<string,string?> values):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)=>builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(values));
    }
}

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Quoridor.Tests;

internal sealed class TestDatabase : IDisposable
{
    private readonly string? adminConnection;
    private readonly string? schema;
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(),"quoridor-tests-"+Guid.NewGuid());
    public Dictionary<string,string?> Values { get; }
    public TestDatabase(string provider)
    {
        Values = new() { ["Persistence:Provider"] = provider, ["ConnectionStrings:SQLite"] = "Data Source=quoridor.db", ["DataDirectory"] = DirectoryPath };
        if (provider == "PostgreSql")
        {
            adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
                ?? throw new InvalidOperationException("PostgreSQL integration tests require TEST_POSTGRES_CONNECTION_STRING pointing to an isolated real PostgreSQL server. See docs/LEADERBOARD.md.");
            schema = "quoridor_test_"+Guid.NewGuid().ToString("N");
            using var connection = new NpgsqlConnection(adminConnection); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = $"CREATE SCHEMA {schema}"; command.ExecuteNonQuery();
            var options = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema };
            Values["ConnectionStrings:PostgreSql"] = options.ToString();
        }
    }
    public IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(Values).Build();
    public DbConnection Open()
    {
        DbConnection connection = schema is null ? new SqliteConnection($"Data Source={Path.Combine(DirectoryPath,"quoridor.db")}") : new NpgsqlConnection(Values["ConnectionStrings:PostgreSql"]);
        connection.Open(); return connection;
    }
    public void Dispose()
    {
        if(schema is not null)
        {
            using var connection = new NpgsqlConnection(adminConnection); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = $"DROP SCHEMA {schema} CASCADE"; command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        if(Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath,true);
    }
}

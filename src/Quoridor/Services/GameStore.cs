using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Quoridor.Services;

public record LeaderboardEntry(Guid PlayerId, string Name, long Wins, long Games);
public record MatchResult(Guid MatchId, Guid RoomId, Guid WinnerId, string WinnerName, Guid OpponentId, string OpponentName, DateTimeOffset FinishedAt);

public sealed class GameStore : IDisposable
{
    private const string SqliteProvider = "SQLite";
    private readonly Func<DbConnection> createConnection;
    private readonly NpgsqlDataSource? dataSource;
    public GameStore(IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? SqliteProvider;
        if (provider.Equals(SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            var raw = configuration.GetConnectionString(SqliteProvider);
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException($"ConnectionStrings:{SqliteProvider} is required for {SqliteProvider} persistence.");
            var options = new SqliteConnectionStringBuilder(raw);
            if (string.IsNullOrWhiteSpace(options.DataSource) || options.DataSource == ":memory:" || options.Mode == SqliteOpenMode.Memory)
                throw new InvalidOperationException($"{SqliteProvider} persistence requires a file Data Source.");
            var directory = Path.GetFullPath(configuration["DataDirectory"] ?? "data");
            options.DataSource = Path.GetFullPath(options.DataSource, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(options.DataSource)!);
            var connectionString = options.ToString();
            createConnection = () => new SqliteConnection(connectionString);
        }
        else if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            var raw = configuration.GetConnectionString("PostgreSql");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("ConnectionStrings:PostgreSql is required for PostgreSql persistence.");
            var options = new NpgsqlConnectionStringBuilder(raw);
            if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.Database))
                throw new InvalidOperationException("PostgreSql persistence requires Host and Database.");
            dataSource = NpgsqlDataSource.Create(options.ConnectionString);
            createConnection = () => dataSource.CreateConnection();
        }
        else throw new InvalidOperationException($"Persistence:Provider must be {SqliteProvider} or PostgreSql.");
        try { Initialize(provider); }
        catch { dataSource?.Dispose(); throw; }
    }
    private void Initialize(string provider)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (provider.Equals(SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = "PRAGMA journal_mode=WAL";
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Players (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Matches (
                MatchId TEXT PRIMARY KEY, RoomId TEXT NOT NULL, WinnerId TEXT NOT NULL,
                WinnerName TEXT NOT NULL, OpponentId TEXT NOT NULL, OpponentName TEXT NOT NULL,
                FinishedAt TEXT NOT NULL, CHECK (WinnerId <> OpponentId));
            CREATE INDEX IF NOT EXISTS IX_Matches_Winner ON Matches(WinnerId);
            CREATE INDEX IF NOT EXISTS IX_Matches_Opponent ON Matches(OpponentId);
            """;
        command.ExecuteNonQuery();
    }
    private DbConnection Open()
    {
        var connection = createConnection();
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
    private static void Add(DbCommand command,string name,object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
    public void Dispose() => dataSource?.Dispose();
    public Player? GetPlayer(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Players WHERE Id=@id"; Add(command, "@id", id.ToString());
        return command.ExecuteScalar() is string name ? new Player(id, name, "UTC") : null;
    }
    public Player SetName(Guid id, string name)
    {
        name = name.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw new ArgumentException("Use a name between 1 and 40 characters without control characters.", nameof(name));
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Players(Id,Name) VALUES(@id,@name) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name";
        Add(command, "@id", id.ToString()); Add(command, "@name", name); command.ExecuteNonQuery();
        return new Player(id, name, "UTC");
    }
    public bool Complete(MatchResult result)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Matches(MatchId,RoomId,WinnerId,WinnerName,OpponentId,OpponentName,FinishedAt)
            VALUES(@match,@room,@winner,@winnerName,@opponent,@opponentName,@at) ON CONFLICT(MatchId) DO NOTHING
            """;
        Add(command, "@match", result.MatchId.ToString()); Add(command, "@room", result.RoomId.ToString());
        Add(command, "@winner", result.WinnerId.ToString()); Add(command, "@winnerName", result.WinnerName);
        Add(command, "@opponent", result.OpponentId.ToString()); Add(command, "@opponentName", result.OpponentName);
        Add(command, "@at", result.FinishedAt.ToUniversalTime().ToString("O"));
        var inserted = command.ExecuteNonQuery() == 1; transaction.Commit(); return inserted;
    }
    public IReadOnlyList<LeaderboardEntry> Leaderboard()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id,p.Name,COUNT(CASE WHEN m.WinnerId=p.Id THEN 1 END),COUNT(m.MatchId)
            FROM Players p JOIN Matches m ON p.Id=m.WinnerId OR p.Id=m.OpponentId
            GROUP BY p.Id,p.Name ORDER BY 3 DESC,4 DESC,LOWER(p.Name),p.Id LIMIT 100
            """;
        using var reader = command.ExecuteReader(); var rows = new List<LeaderboardEntry>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        return rows;
    }
    public IReadOnlyList<MatchResult> History(Guid playerId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT MatchId,RoomId,WinnerId,WinnerName,OpponentId,OpponentName,FinishedAt FROM Matches WHERE WinnerId=@id OR OpponentId=@id ORDER BY FinishedAt DESC,MatchId DESC LIMIT 100";
        Add(command, "@id", playerId.ToString());
        using var reader = command.ExecuteReader(); var rows = new List<MatchResult>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3), Guid.Parse(reader.GetString(4)), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }
}

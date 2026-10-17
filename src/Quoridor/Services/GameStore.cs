using Microsoft.Data.Sqlite;

namespace Quoridor.Services;

public record LeaderboardEntry(Guid PlayerId, string Name, long Wins, long Games);
public record MatchResult(Guid MatchId, Guid RoomId, Guid WinnerId, string WinnerName, Guid OpponentId, string OpponentName, DateTimeOffset FinishedAt);

public sealed class GameStore
{
    private readonly string connectionString;
    public GameStore(IConfiguration configuration)
    {
        var directory = Path.GetFullPath(configuration["DataDirectory"] ?? "data");
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "quoridor.db"), DefaultTimeout = 30 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
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
    private SqliteConnection Open() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }
    public Player? GetPlayer(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Players WHERE Id=$id"; command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is string name ? new Player(id, name, "UTC") : null;
    }
    public Player SetName(Guid id, string name)
    {
        name = name.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw new ArgumentException("Use a name between 1 and 40 characters without control characters.", nameof(name));
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Players(Id,Name) VALUES($id,$name) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name";
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$name", name); command.ExecuteNonQuery();
        return new Player(id, name, "UTC");
    }
    public bool Complete(MatchResult result)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Matches(MatchId,RoomId,WinnerId,WinnerName,OpponentId,OpponentName,FinishedAt)
            VALUES($match,$room,$winner,$winnerName,$opponent,$opponentName,$at) ON CONFLICT(MatchId) DO NOTHING
            """;
        command.Parameters.AddWithValue("$match", result.MatchId.ToString()); command.Parameters.AddWithValue("$room", result.RoomId.ToString());
        command.Parameters.AddWithValue("$winner", result.WinnerId.ToString()); command.Parameters.AddWithValue("$winnerName", result.WinnerName);
        command.Parameters.AddWithValue("$opponent", result.OpponentId.ToString()); command.Parameters.AddWithValue("$opponentName", result.OpponentName);
        command.Parameters.AddWithValue("$at", result.FinishedAt.ToString("O"));
        var inserted = command.ExecuteNonQuery() == 1; transaction.Commit(); return inserted;
    }
    public IReadOnlyList<LeaderboardEntry> Leaderboard()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id,p.Name,COUNT(CASE WHEN m.WinnerId=p.Id THEN 1 END),COUNT(m.MatchId)
            FROM Players p JOIN Matches m ON p.Id=m.WinnerId OR p.Id=m.OpponentId
            GROUP BY p.Id,p.Name ORDER BY 3 DESC,4 DESC,p.Name COLLATE NOCASE,p.Id LIMIT 100
            """;
        using var reader = command.ExecuteReader(); var rows = new List<LeaderboardEntry>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        return rows;
    }
    public IReadOnlyList<MatchResult> History(Guid playerId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT MatchId,RoomId,WinnerId,WinnerName,OpponentId,OpponentName,FinishedAt FROM Matches WHERE WinnerId=$id OR OpponentId=$id ORDER BY FinishedAt DESC,MatchId DESC LIMIT 100";
        command.Parameters.AddWithValue("$id", playerId.ToString());
        using var reader = command.ExecuteReader(); var rows = new List<MatchResult>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3), Guid.Parse(reader.GetString(4)), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }
}
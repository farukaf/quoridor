using Microsoft.Extensions.Configuration;
using Quoridor.Services;
using Quoridor.ViewModels.Board;

namespace Quoridor.Tests;

public sealed class GameTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "quoridor-tests-" + Guid.NewGuid());
    private readonly GameStore store;
    private readonly FrozenClock clock = new();
    public GameTests() => store = Store();
    private GameStore Store() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["DataDirectory"] = directory }).Build());
    private async Task<(RoomViewModel Room, Player Alice, Player Bob)> Game()
    {
        var alice = store.SetName(Guid.NewGuid(), "Alice"); var bob = store.SetName(Guid.NewGuid(), "Bob");
        var room = new RoomViewModel(Guid.NewGuid(), store, clock); await room.EnterRoom(alice); await room.EnterRoom(bob); return (room,alice,bob);
    }
    private static async Task Move(RoomViewModel room, Player actor, int row, int column)
    { var player = actor.Id == room.Player1!.Id ? room.Player1 : room.Player2!; await room.ClickCell(actor.Id,player.Address); await room.ClickCell(actor.Id,new(row,column)); }

    [Fact]
    public async Task CompletedRoundIsRecordedOnceAndSurvivesStoreReopen()
    {
        var (room,alice,bob) = await Game(); var match = room.MatchId;
        await Move(room,alice,0,3); await Move(room,bob,8,5);
        for (var row=1;row<=8;row++)
        { await Move(room,alice,row,3); if(row<8) await Move(room,bob,8,row%2==1?6:5); }
        Assert.NotEqual(match,room.MatchId);
        await Task.WhenAll(Enumerable.Range(0,20).Select(_ => room.ClickCell(alice.Id,new(8,3))));
        var reopened = Store();
        var history = Assert.Single(reopened.History(alice.Id)); Assert.Equal(match,history.MatchId); Assert.Equal(clock.GetUtcNow(),history.FinishedAt);
        Assert.Equal(1,Assert.Single(reopened.Leaderboard(),x=>x.PlayerId==alice.Id).Wins);
        Assert.Single(reopened.History(bob.Id)); Assert.Single(room.Victories);
    }
    [Fact]
    public async Task SpectatorsWrongTurnsAndForgedDestinationsCannotMutateGame()
    {
        var (room,alice,bob) = await Game(); var stranger = Guid.NewGuid();
        await room.ClickCell(alice.Id,new(0,4));
        await Task.WhenAll(room.ClickCell(bob.Id,new(1,4)),room.ClickCell(stranger,new(1,4)),room.ClickCorner(stranger,new(1,1)),room.ClickCell(alice.Id,new(-1,20)));
        Assert.Equal(new(0,4),room.Player1!.Address); Assert.Equal(alice.Id,room.Board.CurrentPlayer!.Id); Assert.Empty(store.Leaderboard());
        // Shared highlight is never sufficient authorization; all destinations are recomputed.
        room.Board.Cells[new(8,0)].State=CellState.AvaliableMove;
        await room.ClickCell(alice.Id,new(8,0)); Assert.Equal(new(0,4),room.Player1.Address);
    }
    [Fact]
    public async Task ConcurrentDuplicateMovesOnlyAdvanceOneTurnAndCrossingWallIsRejected()
    {
        var (room,alice,bob)=await Game();
        await room.ClickCell(alice.Id,new(0,4));
        await Task.WhenAll(Enumerable.Range(0,25).Select(_=>room.ClickCell(alice.Id,new(1,4))));
        Assert.Equal(new(1,4),room.Player1!.Address); Assert.Equal(bob.Id,room.Board.CurrentPlayer!.Id);
        await room.ClickCorner(bob.Id,new(1,0)); await room.ClickCorner(bob.Id,new(1,2));
        Assert.Equal(9,room.Player2!.WallCount);
        await room.ClickCorner(alice.Id,new(0,1)); await room.ClickCorner(alice.Id,new(2,1));
        Assert.Equal(10,room.Player1.WallCount); Assert.Equal(alice.Id,room.Board.CurrentPlayer!.Id);
    }
    [Fact]
    public async Task ConcurrentStoreCompletionsHaveExactlyOneWinnerAndNamesAreParameters()
    {
        var alice=store.SetName(Guid.NewGuid(),"<script>' ; DROP TABLE Players; --"); var bob=store.SetName(Guid.NewGuid(),"Bob");
        var result=new MatchResult(Guid.NewGuid(),Guid.NewGuid(),alice.Id,alice.UserName,bob.Id,bob.UserName,clock.GetUtcNow());
        var inserted=await Task.WhenAll(Enumerable.Range(0,15).Select(_=>Task.Run(()=>store.Complete(result))));
        Assert.Single(inserted,x=>x); Assert.Single(Store().History(alice.Id)); Assert.Equal(alice,Store().GetPlayer(alice.Id));
        Assert.Throws<ArgumentException>(()=>store.SetName(alice.Id,"\n")); Assert.Throws<ArgumentException>(()=>store.SetName(alice.Id,new string('x',41)));
    }
    [Fact]
    public async Task WallCannotEliminateLastPathAndPawnCanJumpOverOpponent()
    {
        var (room,alice,bob)=await Game();
        await room.ClickCorner(alice.Id,new(1,4)); await room.ClickCorner(alice.Id,new(1,6));
        await room.ClickCorner(bob.Id,new(0,4)); await room.ClickCorner(bob.Id,new(2,4));
        await room.ClickCorner(alice.Id,new(0,6)); await room.ClickCorner(alice.Id,new(2,6));
        Assert.Equal(9,room.Player1!.WallCount); Assert.Equal(alice.Id,room.Board.CurrentPlayer!.Id);
        var (jump,a,b)=await Game();
        for(var row=1;row<=3;row++) { await Move(jump,a,row,4); await Move(jump,b,8-row,4); }
        await Move(jump,a,4,4); await Move(jump,b,3,4);
        Assert.Equal(new(3,4),jump.Player2!.Address);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotResetOrReportVictoryAndCanRetrySameMatch()
    {
        var (room,alice,bob)=await Game(); var match=room.MatchId;
        await Move(room,alice,0,3); await Move(room,bob,8,5);
        for(var row=1;row<=7;row++) { await Move(room,alice,row,3); await Move(room,bob,8,row%2==1?6:5); }
        await room.ClickCell(alice.Id,new(7,3));
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(directory,"quoridor.db")}"))
        { connection.Open(); using var command=connection.CreateCommand(); command.CommandText="DROP TABLE Matches"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(()=>room.ClickCell(alice.Id,new(8,3)));
        Assert.Equal(match,room.MatchId); Assert.Equal(new(7,3),room.Player1!.Address); Assert.Empty(room.Victories);
        _=Store(); await room.ClickCell(alice.Id,new(8,3));
        Assert.Equal(match,Assert.Single(store.History(alice.Id)).MatchId); Assert.Single(room.Victories);
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory,true); }
    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,10,17,12,0,0,TimeSpan.Zero); }
}

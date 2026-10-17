using Quoridor.Helper;
using Quoridor.Services;

namespace Quoridor.ViewModels.Board;

public sealed class RoomViewModel
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly GameStore store;
    private readonly TimeProvider clock;
    public RoomViewModel(Guid id, GameStore store, TimeProvider clock)
    { Id = id; this.store = store; this.clock = clock; Board = new BoardViewModel(this); }
    public Guid Id { get; }
    public Guid MatchId { get; private set; } = Guid.NewGuid();
    public RoomConfigurationViewModel Configuration { get; } = new(10);
    public BoardViewModel Board { get; }
    public PlayerViewModel? Player1 { get; private set; }
    public PlayerViewModel? Player2 { get; private set; }
    public Dictionary<CellAddress, PlayerViewModel> Players { get; private set; } = new();
    private Dictionary<Guid, Player> Spectators { get; } = new();
    public IReadOnlyList<Victory> Victories { get; private set; } = Array.Empty<Victory>();
    public Func<Task>? RoomChanged { get; set; }
    public PlayerViewModel? GetPlayer(CellAddress address) => Players.GetValueOrDefault(address);
    public IEnumerable<Player> GetSpectators() { gate.Wait(); try { return Spectators.Values.ToArray(); } finally { gate.Release(); } }
    public bool CanPlay(Guid actor) => Player1 is not null && Player2 is not null && Board.CurrentPlayer?.Id == actor;
    public async Task EnterRoom(Player player)
    {
        await gate.WaitAsync();
        try
        {
            if (Players.Values.All(p => p.Id != player.Id))
            {
                if (Player1 is null || Player2 is null)
                {
                    var first = Player1 is null;
                    var view = new PlayerViewModel(player) { Address = first ? CellAddress.Player1StartPosition : CellAddress.Player2StartPosition, Color = first ? ColorHelper.Player1Color : ColorHelper.Player2Color, WallCount = Configuration.WallsPerPlayer };
                    Players = new(Players) { [view.Address] = view };
                    if (first) { Player1 = view; Board.CurrentPlayer = view; } else Player2 = view;
                }
                else Spectators[player.Id] = player;
            }
        }
        finally { gate.Release(); }
        await Notify();
    }
    public async Task LeaveRoom(Player player)
    {
        await gate.WaitAsync(); try { Spectators.Remove(player.Id); } finally { gate.Release(); }
        await Notify();
    }
    public async Task ClickCell(Guid actor, CellAddress address)
    {
        await gate.WaitAsync();
        try
        {
            if (!CanPlay(actor) || !Board.Cells.TryGetValue(address, out var cell)) return;
            await Board.HandleCell(cell, actor);
        }
        finally { gate.Release(); }
        await Notify();
    }
    public async Task ClickCorner(Guid actor, CornerAddress address)
    {
        await gate.WaitAsync();
        try
        {
            if (!CanPlay(actor) || !Board.Corners.TryGetValue(address, out var corner)) return;
            await Board.HandleCorner(corner);
        }
        finally { gate.Release(); }
        await Notify();
    }
    internal void MoveCurrentPlayer(CellAddress address)
    {
        var player = Board.CurrentPlayer!;
        var winning = ValidateVictory(player, address);
        if (winning)
        {
            var opponent = player == Player1 ? Player2! : Player1!;
            var finished = clock.GetUtcNow();
            // Commit before resetting or exposing victory. An ambiguous retry uses this same MatchId.
            store.Complete(new(MatchId, Id, player.Id, player.Name, opponent.Id, opponent.Name, finished));
            Victories = Victories.Append(new Victory(finished, player.Name, player.Id)).ToArray();
            ResetBoard();
            MatchId = Guid.NewGuid();
            return;
        }
        var players = new Dictionary<CellAddress, PlayerViewModel>(Players);
        players.Remove(player.Address); player.Address = address; players.Add(address, player); Players = players;
        ChangePlayerTurn();
    }
    public bool ValidateVictory(PlayerViewModel player, CellAddress address) => Board.GetCell(address)?.VictoryCondition.PlayerColors.Contains(player.Color) == true;
    internal void WallPlaced()
    {
        Board.CurrentPlayer!.WallCount--;
        ChangePlayerTurn();
    }
    private void ChangePlayerTurn() { Board.CurrentPlayer = Board.CurrentPlayer == Player1 ? Player2 : Player1; Board.ClearSelection(); }
    private void ResetBoard()
    {
        Board.Reset(); var players = new Dictionary<CellAddress, PlayerViewModel>();
        foreach (var player in new[] { Player1!, Player2! })
        { player.Address = player == Player1 ? CellAddress.Player1StartPosition : CellAddress.Player2StartPosition; player.WallCount = Configuration.WallsPerPlayer; players.Add(player.Address, player); }
        Players = players;
        Board.CurrentPlayer = Player1;
    }
    private async Task Notify()
    {
        if (RoomChanged is not null) foreach (Func<Task> handler in RoomChanged.GetInvocationList()) await handler();
    }
}
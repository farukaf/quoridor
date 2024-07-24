﻿using Quoridor.Helper;
using Quoridor.Services;

namespace Quoridor.ViewModels.Board;

public record RoomViewModel : IDisposable
{
    public RoomViewModel()
    {
        Board = new();
        Board.WallPlacedEvent += WallPlaced_Event;
    }

    public Func<Task>? RoomChanged { get; set; }

    public Guid Id { get; internal set; } = Guid.NewGuid();

    public RoomConfigurationViewModel Configuration { get; set; } = new();

    public Dictionary<CellAddress, PlayerViewModel> Players { get; set; } = new();

    public Dictionary<Guid, Player> Spectators { get; set; } = new();

    public BoardViewModel Board { get; set; }

    public PlayerViewModel? GetPlayer(CellAddress address)
    {
        return Players.TryGetValue(address, out var player) ? player : null;
    }

    public PlayerViewModel? Player1 { get; private set; }
    public PlayerViewModel? Player2 { get; private set; }

    public IEnumerable<PlayerViewModel> GetPlayers() =>
        Players.Values;

    public IEnumerable<Player> GetSpectators() =>
        Spectators.Values;

    public KeyValuePair<CellAddress, PlayerViewModel>? GetPlayer(Player player) =>
        Players.FirstOrDefault(p => p.Value.Id == player.Id);

    public void MovePlayer(CellAddress from, CellAddress to)
    {
        if (Players.TryGetValue(from, out var player))
        {
            if (Players.TryAdd(to, player))
            {
                player.Address = to;
                Players.Remove(from);
            }
            RoomChanged?.Invoke();
        }
    }

    public void LeaveRoom(Player player)
    {
        Spectators.Remove(player.Id);
        RoomChanged?.Invoke();
    }

    public void EnterRoom(Player player)
    {
        PlayerViewModel? playerViewModel = EnterRoom_GetPlayer(player);

        if (playerViewModel is null)
        {
            Spectators.TryAdd(player.Id, player);
        }

        RoomChanged?.Invoke();
    }

    private PlayerViewModel? EnterRoom_GetPlayer(Player player)
    {
        var playerKv = GetPlayer(player);
        var playerViewModel = playerKv?.Value;

        if (playerViewModel is null)
        {
            if (Players.Count == 1)
            {
                //Add Player 2
                playerViewModel = new PlayerViewModel(player);
                playerViewModel.Address = CellAddress.Player2StartPosition;
                playerViewModel.Color = Color.Red;
                playerViewModel.WallCount = Configuration.WallsPerPlayer;
                Players.Add(playerViewModel.Address, playerViewModel);
                Player2 = playerViewModel;
            }
            if (Players.Count == 0)
            {
                //Add Player 1 
                playerViewModel = new PlayerViewModel(player);
                playerViewModel.Address = CellAddress.Player1StartPosition;
                playerViewModel.Color = Color.Blue;
                playerViewModel.WallCount = Configuration.WallsPerPlayer;
                Players.Add(playerViewModel.Address, playerViewModel);
                Board.CurrentPlayer = playerViewModel;
                Player1 = playerViewModel;
            }
        }

        return playerViewModel;
    }

    private void ChangePlayerTurn()
    {
        if (Board.CurrentPlayer is null)
            return;

        switch (Board.CurrentPlayer)
        {
            case { } player when player == Player1:
                Board.CurrentPlayer = Player2;
                break;
            case { } player when player == Player2:
                Board.CurrentPlayer = Player1;
                break;
        }
    }

    private Task WallPlaced_Event()
    {
        if (Board.CurrentPlayer is null)
            return Task.CompletedTask;

        Board.CurrentPlayer.WallCount--;
        ChangePlayerTurn();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Board.WallPlacedEvent -= WallPlaced_Event;
        Players?.Clear();
        Players?.TrimExcess();
        Spectators?.Clear();
        Spectators?.TrimExcess();
    }
}

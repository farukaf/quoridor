using Quoridor.Helper;
using Quoridor.Services;

namespace Quoridor.ViewModels.Board;

public record BoardViewModel : IDisposable
{
    public BoardViewModel(RoomViewModel room)
    {
        RowSize = 9;
        ColumnSize = 9;
        GenerateCorners();
        GenerateWalls();
        GenerateCells();
        GenerateGridAddress();
        Room = room;
    }

    public PlayerViewModel? CurrentPlayer { get; set; }
    public RoomViewModel Room { get; set; }
    public short RowSize { get; set; }
    public short ColumnSize { get; set; }
    private List<WallViewModel> Walls { get; set; } = new();
    public Dictionary<GridAddress, GridElementViewModel> GridElements { get; set; } = new();
    public Dictionary<CellAddress, CellViewModel> Cells { get; set; } = new();
    public Dictionary<CornerAddress, CornerViewModel> Corners { get; set; } = new();



    public CellViewModel? GetCell(CellAddress address)
    {
        return Cells.TryGetValue(address, out var cell) ? cell : null;
    }

    #region CornerClick_Management
    internal async Task HandleCorner(CornerViewModel clickedCorner)
    {
        switch (clickedCorner.State)
        {
            case CornerState.Enabled:
                await EnabledCornerClicked(clickedCorner);
                break;
            case CornerState.Selected:
                await SelectedCornerClicked(clickedCorner);
                break;
            case CornerState.Disabled:
                await DisabledCornerClicked(clickedCorner);
                break;
            case CornerState.Avaliable:
                await AvaliableCornerClicked(clickedCorner);
                break;
        }


    }

    private (IEnumerable<CornerViewModel> corners, WallViewModel wall1, WallViewModel wall2) GetWallsFromCorners(CornerViewModel corner1, CornerViewModel corner2)
    {
        //Find the corner in between selected and clicked
        var middleCorner = Corners.Values.First(c =>
        {
            if (corner1.CornerAddress.X == corner2.CornerAddress.X && corner1.CornerAddress.X == c.CornerAddress.X)
                return (c.CornerAddress.Y > corner1.CornerAddress.Y && c.CornerAddress.Y < corner2.CornerAddress.Y) ||
                    (c.CornerAddress.Y < corner1.CornerAddress.Y && c.CornerAddress.Y > corner2.CornerAddress.Y);

            if (corner1.CornerAddress.Y == corner2.CornerAddress.Y && corner1.CornerAddress.Y == c.CornerAddress.Y)
                return (c.CornerAddress.X > corner1.CornerAddress.X && c.CornerAddress.X < corner2.CornerAddress.X) ||
                    (c.CornerAddress.X < corner1.CornerAddress.X && c.CornerAddress.X > corner2.CornerAddress.X);

            return false;
        });

        var corners = (new[] { corner2, corner1, middleCorner })
            .Where(x => x is not null)
            .OrderBy(x => x.CornerAddress.X)
            .ThenBy(x => x.CornerAddress.Y);

        var wall1 = Walls.Single(w => w.From == corners.First().CornerAddress && w.To == corners.ElementAt(1).CornerAddress);
        var wall2 = Walls.Single(w => w.From == corners.ElementAt(1).CornerAddress && w.To == corners.Last().CornerAddress);

        return (corners, wall1, wall2);
    }

    private Task DisabledCornerClicked(CornerViewModel clickedCorner)
    {
        return Task.CompletedTask;
    }

    private Task SelectedCornerClicked(CornerViewModel clickedCorner)
    {
        foreach (var corner in Corners.Values)
        {
            //TODO: Check if colides with a wall
            corner.State = CornerState.Enabled;
        }

        return Task.CompletedTask;
    }

    private Task EnabledCornerClicked(CornerViewModel clickedCorner)
    {
        var currentPlayerWallCount = CurrentPlayer?.WallCount ?? 0;
        if (currentPlayerWallCount <= 0)
            return Task.CompletedTask;

        var avaliable = 0;
        clickedCorner.State = CornerState.Selected;
        foreach (var corner in Corners.Values)
        {
            if (corner == clickedCorner)
                continue;

            corner.State = CornerState.Disabled;

            if ((corner.CornerAddress.X == clickedCorner.CornerAddress.X && corner.CornerAddress.Y == clickedCorner.CornerAddress.Y + 2) ||
                (corner.CornerAddress.Y == clickedCorner.CornerAddress.Y && corner.CornerAddress.X == clickedCorner.CornerAddress.X + 2) ||
                (corner.CornerAddress.X == clickedCorner.CornerAddress.X && corner.CornerAddress.Y == clickedCorner.CornerAddress.Y - 2) ||
                (corner.CornerAddress.Y == clickedCorner.CornerAddress.Y && corner.CornerAddress.X == clickedCorner.CornerAddress.X - 2))
            {
                if (!ValidWallPosition(clickedCorner, corner)) continue;
                (var corners, var wall1, var wall2) = GetWallsFromCorners(clickedCorner, corner);

                if (wall1.IsPlaced || wall2.IsPlaced || CrossesWall(clickedCorner, corner))
                    continue;

                //If performace is a issue, look this first :D
                var willBlockPlayer = WillBlockPlayer(clickedCorner, corner, corners, wall1, wall2);
                if (willBlockPlayer)
                    continue;

                corner.State = CornerState.Avaliable;
                avaliable++;
                continue;
            }
        }

        if (avaliable == 0)
        {
            foreach (var corner in Corners.Values)
                corner.State = CornerState.Enabled;
        }

        return Task.CompletedTask;
    }

    private static bool ValidWallPosition(CornerViewModel a, CornerViewModel b) =>
        a.CornerAddress.X == b.CornerAddress.X ? a.CornerAddress.X is > 0 and < 9 : a.CornerAddress.Y is > 0 and < 9;

    private bool CrossesWall(CornerViewModel first, CornerViewModel second)
    {
        var x = (first.CornerAddress.X + second.CornerAddress.X) / 2;
        var y = (first.CornerAddress.Y + second.CornerAddress.Y) / 2;
        var horizontal = first.CornerAddress.X == second.CornerAddress.X;
        return Walls.Count(w => w.IsPlaced && (horizontal ? w.Direction == Direction.Vertical : w.Direction == Direction.Horizontal)
            && (w.From == new CornerAddress((short)x, (short)y) || w.To == new CornerAddress((short)x, (short)y))) == 2;
    }

    private bool WillBlockPlayer(CornerViewModel clickedCorner, CornerViewModel corner, IEnumerable<CornerViewModel> corners, WallViewModel wall1, WallViewModel wall2)
    {
        // Simulate placing the walls to the simplify the latter algorithm
        wall1.IsPlaced = true;
        wall2.IsPlaced = true;

        // Check if either player is blocked
        bool player1Blocked = IsPlayerBlocked(Room.Player1);
        bool player2Blocked = IsPlayerBlocked(Room.Player2);

        // Revert the simulated wall placements
        wall1.IsPlaced = false;
        wall2.IsPlaced = false;

        return player1Blocked || player2Blocked;
    }

    Queue<CellAddress> _isPlayerBlockedQueue = new Queue<CellAddress>();
    HashSet<CellAddress> _isPlayerBlockedVisited = new HashSet<CellAddress>();

    /// <summary>
    /// This uses a breadth-first search (BFS) to determine if a player can reach any of their victory cells
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    private bool IsPlayerBlocked(PlayerViewModel? player)
    {
        if (player == null)
            return false;

        _isPlayerBlockedVisited.Clear();
        _isPlayerBlockedQueue.Clear();
        _isPlayerBlockedQueue.Enqueue(player.Address);

        while (_isPlayerBlockedQueue.Count > 0)
        {
            var current = _isPlayerBlockedQueue.Dequeue();
            if (_isPlayerBlockedVisited.Contains(current))
                continue;

            _isPlayerBlockedVisited.Add(current);

            if (Room.ValidateVictory(player, current))
                return false;

            var neighbors = GetNeighbors(current);
            foreach (var neighbor in neighbors)
            {
                if (!_isPlayerBlockedVisited.Contains(neighbor))
                    _isPlayerBlockedQueue.Enqueue(neighbor);
            }
        }

        return true;
    }

    private IEnumerable<CellAddress> GetNeighbors(CellAddress address)
    {
        var neighbors = new List<CellAddress>();

        var cell = GetCell(address);
        if (cell is null)
            return neighbors;

        if (!cell.TopWall.IsPlaced)
            neighbors.Add(new CellAddress { Row = (short)(address.Row - 1), Column = address.Column });
        if (!cell.BottomWall.IsPlaced)
            neighbors.Add(new CellAddress { Row = (short)(address.Row + 1), Column = address.Column });
        if (!cell.LeftWall.IsPlaced)
            neighbors.Add(new CellAddress { Row = address.Row, Column = (short)(address.Column - 1) });
        if (!cell.RightWall.IsPlaced)
            neighbors.Add(new CellAddress { Row = address.Row, Column = (short)(address.Column + 1) });

        return neighbors;
    }

    private async Task AvaliableCornerClicked(CornerViewModel clickedCorner)
    {
        var selectedCorner = Corners.Values.First(c => c.State == CornerState.Selected);

        (var corners, var wall1, var wall2) = GetWallsFromCorners(clickedCorner, selectedCorner);
        if (wall1.IsPlaced || wall2.IsPlaced || CrossesWall(clickedCorner, selectedCorner) || WillBlockPlayer(clickedCorner, selectedCorner, corners, wall1, wall2)) return;

        wall1.IsPlaced = true;
        wall2.IsPlaced = true;

        //Reset the corners
        foreach (var corner in Corners.Values)
            corner.State = CornerState.Enabled;

        Room.WallPlaced();
        await Task.CompletedTask;
    }

    #endregion

    #region CellClicked_Management

    internal async Task HandleCell(CellViewModel cell, Guid actor)
    {
        if (!Room.CanPlay(actor) || CurrentPlayer is null) return;
        var legal = LegalMoves(CurrentPlayer.Address).ToHashSet();
        if (cell.State == CellState.AvaliableMove && legal.Contains(cell.Address))
        {
            Room.MoveCurrentPlayer(cell.Address);
            ClearSelection();
        }
        else if (cell.Address == CurrentPlayer.Address)
        {
            var selected = Cells.Values.Any(c => c.State == CellState.AvaliableMove);
            ClearSelection();
            if (!selected) foreach (var address in legal) Cells[address].State = CellState.AvaliableMove;
        }
        await Task.CompletedTask;
    }

    internal IEnumerable<CellAddress> LegalMoves(CellAddress source)
    {
        foreach (var next in GetNeighbors(source).Where(Cells.ContainsKey))
        {
            if (!Room.Players.ContainsKey(next)) { yield return next; continue; }
            var beyond = new CellAddress(next.Row + next.Row - source.Row, next.Column + next.Column - source.Column);
            var neighbors = GetNeighbors(next).Where(Cells.ContainsKey).ToHashSet();
            if (neighbors.Contains(beyond) && !Room.Players.ContainsKey(beyond)) yield return beyond;
            else foreach (var diagonal in neighbors.Where(n => n != source && n.Row != beyond.Row && n.Column != beyond.Column && !Room.Players.ContainsKey(n))) yield return diagonal;
        }
    }
    internal void ClearSelection()
    {
        foreach (var cell in Cells.Values) cell.State = CellState.Clear;
        foreach (var corner in Corners.Values) corner.State = CornerState.Enabled;
    }

    #endregion


    private void GenerateCells()
    {
        for (short i = 0; i < RowSize; i++)
        {
            for (short j = 0; j < ColumnSize; j++)
            {
                var cellAddress = new CellAddress { Row = i, Column = j };

                var cell = new CellViewModel()
                {
                    Address = cellAddress,
                    TopWall = Walls.Single(w => w.BottomCell == cellAddress),
                    LeftWall = Walls.Single(w => w.RightCell == cellAddress),
                    RightWall = Walls.Single(w => w.LeftCell == cellAddress),
                    BottomWall = Walls.Single(w => w.TopCell == cellAddress),
                    VictoryCondition = new(new())
                };



                if (CellAddress.Player1VictoryCells.Contains(new(i, j)))
                    cell.VictoryCondition.PlayerColors.Add(ColorHelper.Player1Color);
                if (CellAddress.Player2VictoryCells.Contains(new(i, j)))
                    cell.VictoryCondition.PlayerColors.Add(ColorHelper.Player2Color);

                Cells.Add(cellAddress, cell);
            }
        }
    }

    private void GenerateCorners()
    {
        short i = 0, j = 0;
        for (; i < RowSize + 1; i++)
        {
            j = 0;
            for (; j < ColumnSize + 1; j++)
            {
                var cornerAddress = new CornerAddress { X = i, Y = j };
                var corner = new CornerViewModel { CornerAddress = cornerAddress };

                Corners.Add(cornerAddress, corner);
            }
        }
    }

    private void GenerateWalls()
    {
        Walls.Clear();
        short i = 0, j = 0;
        for (; i < RowSize; i++)
        {
            j = 0;
            for (; j < ColumnSize; j++)
            {
                //Generatel walls horizontal and above the cell 
                var horSupWall = new WallViewModel
                {
                    TopCell = i > 0 ? new CellAddress((short)(i - 1), j) : null,
                    BottomCell = new CellAddress(i, j),
                    Direction = Direction.Horizontal,
                };

                //genrate wall vertical and to the left the cell 
                var verLftWall = new WallViewModel
                {
                    LeftCell = j > 0 ? new CellAddress(i, (short)(j - 1)) : null,
                    RightCell = new CellAddress(i, j),
                    Direction = Direction.Vertical,
                };

                Walls.Add(horSupWall);
                Walls.Add(verLftWall);
            }

            //Generate wall vertical and to the right the cell
            var verRgtWall = new WallViewModel
            {
                LeftCell = new CellAddress(i, (short)(j - 1)),
                From = new CornerAddress { X = i, Y = j },
                To = new CornerAddress { X = i, Y = (short)(j + 1) },
                Direction = Direction.Vertical,
            };
            Walls.Add(verRgtWall);
        }

        j = 0;
        for (; j < ColumnSize; j++)
        {
            //Generate walls horizontal and under the cell
            var horInfWall = new WallViewModel
            {
                TopCell = new CellAddress((short)(i - 1), j),
                From = new CornerAddress { X = i, Y = j },
                To = new CornerAddress { X = (short)(i + 1), Y = j },
                Direction = Direction.Horizontal,
            };
            Walls.Add(horInfWall);
        }
    }

    public void GenerateGridAddress()
    {
        var rows = RowSize * 2 + 1;
        var columns = ColumnSize * 2 + 1;

        //Resolve Corners First
        for (short i = 0; i < rows; i++)
        {
            for (short j = 0; j < columns; j++)
            {
                var gridAddress = new GridAddress(i, j);
                //even column and even row: corner 
                if (j % 2 == 0 && i % 2 == 0)
                {
                    var cornerAddress = new CornerAddress((short)(i / 2), (short)(j / 2));
                    var corner = Corners[cornerAddress];
                    corner.GridAddress = gridAddress;
                    GridElements.Add(gridAddress, corner);
                    continue;
                }
            }
        }

        for (short i = 0; i < rows; i++)
        {
            for (short j = 0; j < columns; j++)
            {
                var gridAddress = new GridAddress(i, j);
                //even column and even row: corner 
                if (j % 2 == 0 && i % 2 == 0)
                {
                    continue;
                }

                //odd column and odd row: cell
                if (j % 2 != 0 && i % 2 != 0)
                {
                    var cellAddress = new CellAddress((short)(i / 2), (short)(j / 2));
                    var cell = Cells[cellAddress];
                    cell.GridAddress = gridAddress;
                    GridElements.Add(gridAddress, cell);
                    continue;
                }

                //odd column and even row: horizontal wall
                if (j % 2 != 0 && i % 2 == 0)
                {
                    var cellAddress = new CellAddress((short)(i / 2), (short)(j / 2));
                    WallViewModel? wall = null;
                    if (Cells.ContainsKey(cellAddress))
                    {
                        var cell = Cells[cellAddress];
                        wall = cell.TopWall;
                    }
                    else
                    {
                        //this will handle the last row
                        var cell = Cells[new CellAddress((short)(cellAddress.Row - 1), cellAddress.Column)];
                        wall = cell.BottomWall;
                    }
                    wall.GridAddress = gridAddress;
                    GridElements.Add(gridAddress, wall);

                    //resolve corners
                    var cornerAddressFrom = gridAddress with
                    {
                        Column = (short)(gridAddress.Column - 1)
                    };
                    var cornerAddressTo = gridAddress with
                    {
                        Column = (short)(gridAddress.Column + 1)
                    };

                    var cornerFrom = GridElements[cornerAddressFrom] as CornerViewModel;
                    var cornerTo = GridElements[cornerAddressTo] as CornerViewModel;
                    wall.From = cornerFrom!.CornerAddress;
                    wall.To = cornerTo!.CornerAddress;

                    continue;
                }

                //even column and odd row: vertical wall
                if (j % 2 == 0 && i % 2 != 0)
                {
                    var cellAddress = new CellAddress((short)(i / 2), (short)(j / 2));
                    WallViewModel? wall = null;
                    if (Cells.ContainsKey(cellAddress))
                    {
                        var cell = Cells[cellAddress];
                        wall = cell.LeftWall;
                    }
                    else
                    {
                        //this will handle the last column
                        var cell = Cells[new CellAddress(cellAddress.Row, (short)(cellAddress.Column - 1))];
                        wall = cell.RightWall;
                    }
                    wall.GridAddress = gridAddress;
                    GridElements.Add(gridAddress, wall);

                    //resolve corners
                    var cornerAddressFrom = gridAddress with
                    {
                        Row = (short)(gridAddress.Row - 1)
                    };
                    var cornerAddressTo = gridAddress with
                    {
                        Row = (short)(gridAddress.Row + 1)
                    };

                    var cornerFrom = GridElements[cornerAddressFrom] as CornerViewModel;
                    var cornerTo = GridElements[cornerAddressTo] as CornerViewModel;
                    wall.From = cornerFrom!.CornerAddress;
                    wall.To = cornerTo!.CornerAddress;

                    continue;
                }
            }
        }
    }

    public void Dispose()
    {
        //FYI: TrimExcess is a good practice to free memory, but tends to consume cpu instead...
        //Here we are prioritizing memory usage over cpu usage
        Walls.Clear();
        Walls.TrimExcess();
        GridElements.Clear();
        GridElements.TrimExcess();
        Cells.Clear();
        Cells.TrimExcess();
        Corners.Clear();
        Corners.TrimExcess();
        _isPlayerBlockedQueue.Clear();
        _isPlayerBlockedQueue.TrimExcess();
        _isPlayerBlockedVisited.Clear();
        _isPlayerBlockedVisited.TrimExcess();
    }

    public void Reset()
    {
        //Reset the corners
        foreach (var corner in Corners.Values)
            corner.State = CornerState.Enabled;

        foreach (var cell in Cells.Values)
            cell.State = CellState.Clear;

        foreach (var wall in Walls)
            wall.IsPlaced = false;
    }
}

using Quoridor.Services;

namespace Quoridor.ViewModels.Board;

public record CellViewModel : GridElementViewModel
{
    public WallViewModel TopWall { get; set; } = default!;
    public WallViewModel LeftWall { get; set; } = default!;
    public WallViewModel RightWall { get; set; } = default!;
    public WallViewModel BottomWall { get; set; } = default!;
    public CellAddress Address { get; set; }
    public CellState State { get; set; } 
    public VictoryCondition VictoryCondition { get; set; } = default!;

    public override string CssClass()
    {
        return "cell";
    }

    public override string ToString()
    {
        return $"(r{Address.Row},c{Address.Column})";
    }
     

}
 
public enum CellState
{
    Clear,
    AvaliableMove
}
namespace Quoridor.ViewModels.Board;

public record CornerViewModel : GridElementViewModel
{
    public CornerAddress CornerAddress { get; set; }
    public CornerState State { get; set; }

    public override string CssClass()
    {
        return "corner";
    }


}

public enum CornerState
{
    /// <summary>
    /// Start state when the user can start a wall 
    /// </summary>
    Enabled,
    /// <summary>
    /// After is selected the user can select the second corner
    /// </summary>
    Selected,
    /// <summary>
    /// When the corner is not avaliable to start or finish a wall
    /// </summary>
    Disabled,
    /// <summary>
    /// When the corner is avaliable to  finish a wall
    /// </summary>
    Avaliable
}
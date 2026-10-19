using Microsoft.AspNetCore.Components;
using Quoridor.Services;
using Quoridor.ViewModels.Board;

namespace Quoridor.Components.Components.Board;

public partial class PlayerBoard
{
    [Parameter]
    public RoomViewModel Room { get; set; } = default!;

    protected Player? player;
    protected override async Task OnInitializedAsync()
    {
        player = await playerService.GetPlayer();
    }
}

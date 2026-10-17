using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Quoridor.Services;

public interface IPlayerService
{
    Func<Task>? PlayerChanged { get; set; }
    Task<Player?> GetPlayer();
    Task<Player> SetUser(string userName);
}
public record Player(Guid Id, string UserName, string Timezone);

public sealed class PlayerService(AuthenticationStateProvider authentication, GameStore store) : IPlayerService
{
    public Func<Task>? PlayerChanged { get; set; }
    private async Task<Guid> Identity()
    {
        var state = await authentication.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true || !Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("A server-issued player identity is required.");
        return id;
    }
    public async Task<Player?> GetPlayer() => store.GetPlayer(await Identity());
    public async Task<Player> SetUser(string userName)
    {
        var player = store.SetName(await Identity(), userName);
        if (PlayerChanged is not null) foreach (Func<Task> handler in PlayerChanged.GetInvocationList()) await handler();
        return player;
    }
}
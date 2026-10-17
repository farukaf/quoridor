using System.Collections.Concurrent;
using Quoridor.ViewModels.Board;
namespace Quoridor.Services;

public interface IRoomService { RoomViewModel GetRoom(Guid id); }
public sealed class RoomService(GameStore store, TimeProvider clock) : IRoomService
{
    private readonly ConcurrentDictionary<Guid, RoomViewModel> rooms = new();
    public RoomViewModel GetRoom(Guid id) => rooms.GetOrAdd(id, roomId => new RoomViewModel(roomId, store, clock));
}
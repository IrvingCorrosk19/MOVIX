using Movix.Domain.Enums;

namespace Movix.Domain.Trip;

public static class TripStateMachine
{
    private static readonly IReadOnlyDictionary<TripStatus, TripStatus[]> AllowedTransitions = new Dictionary<TripStatus, TripStatus[]>
    {
        [TripStatus.Requested] = new[] { TripStatus.Accepted, TripStatus.Cancelled },
        [TripStatus.Accepted] = new[] { TripStatus.DriverArrived, TripStatus.Cancelled },
        [TripStatus.DriverArrived] = new[] { TripStatus.InProgress, TripStatus.Cancelled },
        [TripStatus.InProgress] = new[] { TripStatus.Completed, TripStatus.Cancelled },
        [TripStatus.Completed] = Array.Empty<TripStatus>(),
        [TripStatus.Cancelled] = Array.Empty<TripStatus>()
    };

    public static bool CanTransition(TripStatus from, TripStatus to)
    {
        if (from == to) return false;
        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static IReadOnlyList<TripStatus> GetAllowedTargets(TripStatus current) =>
        AllowedTransitions.TryGetValue(current, out var allowed) ? allowed : Array.Empty<TripStatus>();
}

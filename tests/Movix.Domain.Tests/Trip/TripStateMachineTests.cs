using Movix.Domain.Enums;
using Movix.Domain.Trip;
using Xunit;

namespace Movix.Domain.Tests.Trip;

public class TripStateMachineTests
{
    [Fact]
    public void CanTransition_Requested_To_Accepted_ReturnsTrue()
    {
        Assert.True(TripStateMachine.CanTransition(TripStatus.Requested, TripStatus.Accepted));
    }

    [Fact]
    public void CanTransition_Requested_To_Cancelled_ReturnsTrue()
    {
        Assert.True(TripStateMachine.CanTransition(TripStatus.Requested, TripStatus.Cancelled));
    }

    [Fact]
    public void CanTransition_Requested_To_InProgress_ReturnsFalse()
    {
        Assert.False(TripStateMachine.CanTransition(TripStatus.Requested, TripStatus.InProgress));
    }

    [Fact]
    public void CanTransition_Completed_To_Any_ReturnsFalse()
    {
        Assert.False(TripStateMachine.CanTransition(TripStatus.Completed, TripStatus.Cancelled));
    }

    [Fact]
    public void CanTransition_SameStatus_ReturnsFalse()
    {
        Assert.False(TripStateMachine.CanTransition(TripStatus.Requested, TripStatus.Requested));
    }
}

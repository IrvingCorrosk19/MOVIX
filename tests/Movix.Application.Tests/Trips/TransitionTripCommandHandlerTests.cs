using Moq;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.TransitionTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class TransitionTripCommandHandlerTests
{
    private readonly Mock<ITripRepository>      _tripRepo    = new();
    private readonly Mock<ICurrentUserService>  _currentUser = new();
    private readonly Mock<IDateTimeService>     _dateTime    = new();
    private readonly Mock<IUnitOfWork>          _uow         = new();

    private TransitionTripCommandHandler CreateHandler() =>
        new(_tripRepo.Object, _currentUser.Object, _dateTime.Object, _uow.Object);

    private static Trip MakeTrip(Guid passengerId, Guid? driverId, TripStatus status) => new()
    {
        Id               = Guid.NewGuid(),
        PassengerId      = passengerId,
        DriverId         = driverId,
        Status           = status,
        IdempotencyKey   = Guid.NewGuid().ToString(),
        Currency         = "USD",
        CreatedAtUtc     = DateTime.UtcNow,
        UpdatedAtUtc     = DateTime.UtcNow,
        CreatedBy        = "test",
        UpdatedBy        = "test",
        RowVersion       = new byte[] { 1 }
    };

    // R-1 — Sin driver asignado no se puede hacer arrive
    [Fact]
    public async Task Arrive_WhenNoDriverAssigned_Returns_DRIVER_NOT_ASSIGNED()
    {
        var trip = MakeTrip(Guid.NewGuid(), driverId: null, TripStatus.Accepted);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.False(result.Succeeded);
        Assert.Equal("DRIVER_NOT_ASSIGNED", result.ErrorCode);
    }

    // R-1 — Driver equivocado no puede hacer arrive
    [Fact]
    public async Task Arrive_WhenWrongDriver_Returns_FORBIDDEN()
    {
        var assignedDriver = Guid.NewGuid();
        var otherDriver    = Guid.NewGuid();
        var trip = MakeTrip(Guid.NewGuid(), assignedDriver, TripStatus.Accepted);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(otherDriver);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    // R-2 — Usuario sin relación con el viaje no puede cancelar
    [Fact]
    public async Task Cancel_WhenRandomUser_Returns_FORBIDDEN()
    {
        var passenger  = Guid.NewGuid();
        var driver     = Guid.NewGuid();
        var randomUser = Guid.NewGuid();
        var trip = MakeTrip(passenger, driver, TripStatus.Requested);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(randomUser);
        _currentUser.Setup(c => c.Role).Returns(Role.Passenger);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Cancelled), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    // R-2 — Pasajero dueño del viaje puede cancelarlo
    [Fact]
    public async Task Cancel_WhenPassengerIsOwner_Succeeds()
    {
        var passenger = Guid.NewGuid();
        var trip = MakeTrip(passenger, driverId: null, TripStatus.Requested);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(passenger);
        _currentUser.Setup(c => c.Role).Returns(Role.Passenger);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Cancelled), default);

        Assert.True(result.Succeeded);
        Assert.Equal("Cancelled", result.Data!.Status);
    }

    // R-1 + R-2 — Admin puede transicionar sin ser el driver asignado
    [Fact]
    public async Task Arrive_WhenAdmin_Succeeds()
    {
        var driverId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var trip = MakeTrip(Guid.NewGuid(), driverId, TripStatus.Accepted);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(adminId);
        _currentUser.Setup(c => c.Role).Returns(Role.Admin);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.True(result.Succeeded);
        Assert.Equal("DriverArrived", result.Data!.Status);
    }

    // C-1 — Modificación concurrente retorna CONFLICT
    [Fact]
    public async Task Arrive_WhenConcurrentModification_Returns_CONFLICT()
    {
        var driverId = Guid.NewGuid();
        var trip = MakeTrip(Guid.NewGuid(), driverId, TripStatus.Accepted);
        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _currentUser.Setup(c => c.UserId).Returns(driverId);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ThrowsAsync(new ConcurrencyException());

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.DriverArrived), default);

        Assert.False(result.Succeeded);
        Assert.Equal("CONFLICT", result.ErrorCode);
    }
}

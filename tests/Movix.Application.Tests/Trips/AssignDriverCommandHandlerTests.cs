using Moq;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.AssignDriver;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

public class AssignDriverCommandHandlerTests
{
    private readonly Mock<ITripRepository> _tripRepo = new();
    private readonly Mock<IDriverAvailabilityRepository> _availRepo = new();
    private readonly Mock<IOutboxMessageRepository> _outboxRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IDateTimeService> _dateTime = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditService> _audit = new();

    private AssignDriverCommandHandler CreateHandler() =>
        new(_tripRepo.Object, _availRepo.Object, _outboxRepo.Object, _currentUser.Object, _tenantContext.Object, _dateTime.Object, _uow.Object, _audit.Object);

    private void SetAdminWithTenant(Guid tenantId)
    {
        _currentUser.Setup(c => c.Role).Returns(Role.Admin);
        _tenantContext.Setup(t => t.IsSuperAdmin).Returns(false);
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);
    }

    private static Trip MakeTrip(Guid? driverId, TripStatus status, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        PassengerId = Guid.NewGuid(),
        DriverId = driverId,
        Status = status,
        TenantId = tenantId,
        IdempotencyKey = Guid.NewGuid().ToString(),
        Currency = "USD",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = new byte[] { 1 }
    };

    [Fact]
    public async Task Handle_WhenTripNotFound_Returns_TRIP_NOT_FOUND()
    {
        SetAdminWithTenant(Guid.NewGuid());
        var tripId = Guid.NewGuid();
        _tripRepo.Setup(r => r.GetByIdAsync(tripId, default)).ReturnsAsync((Trip?)null);

        var result = await CreateHandler().Handle(new AssignDriverCommand(tripId), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TRIP_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenTripNotRequested_Returns_TRIP_INVALID_STATE()
    {
        SetAdminWithTenant(Guid.NewGuid());
        var trip = MakeTrip(null, TripStatus.Accepted);
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TRIP_INVALID_STATE", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenNoDriversAvailable_Returns_NO_DRIVERS_AVAILABLE()
    {
        SetAdminWithTenant(Guid.NewGuid());
        var trip = MakeTrip(null, TripStatus.Requested);
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);
        _availRepo.Setup(r => r.GetFirstAvailableAsync(default)).ReturnsAsync((DriverAvailability?)null);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("NO_DRIVERS_AVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenDriverAvailable_AssignsAndReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        SetAdminWithTenant(tenantId);
        var trip = MakeTrip(null, TripStatus.Requested, tenantId);
        var driverId = Guid.NewGuid();
        var availability = new DriverAvailability
        {
            DriverId = driverId,
            Driver = new Driver { Id = driverId, TenantId = tenantId },
            IsOnline = true,
            CurrentTripId = null,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);
        _availRepo.Setup(r => r.GetFirstAvailableAsync(default)).ReturnsAsync(availability);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(TripStatus.Accepted.ToString(), result.Data.Status);
        Assert.Equal(driverId, trip.DriverId);
        Assert.Equal(trip.Id, availability.CurrentTripId);

        _outboxRepo.Verify(
            r => r.AddAsync(It.Is<OutboxMessage>(m => m.Type == "DriverAssigned" && m.Payload.Contains(trip.Id.ToString()) && m.Payload.Contains(driverId.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSaveThrowsConcurrencyException_Returns_NO_DRIVERS_AVAILABLE()
    {
        var tenantId = Guid.NewGuid();
        SetAdminWithTenant(tenantId);
        var trip = MakeTrip(null, TripStatus.Requested, tenantId);
        var availability = new DriverAvailability
        {
            DriverId = Guid.NewGuid(),
            Driver = new Driver { TenantId = tenantId },
            IsOnline = true,
            CurrentTripId = null,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);
        _availRepo.Setup(r => r.GetFirstAvailableAsync(default)).ReturnsAsync(availability);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ThrowsAsync(new ConcurrencyException());

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("NO_DRIVERS_AVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task AssignDriver_WhenCallerIsPassenger_Returns403()
    {
        _currentUser.Setup(c => c.Role).Returns(Role.Passenger);
        _tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid());
        var trip = MakeTrip(null, TripStatus.Requested, Guid.NewGuid());
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    [Fact]
    public async Task AssignDriver_WhenTripBelongsToOtherTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        SetAdminWithTenant(tenantA);
        var trip = MakeTrip(null, TripStatus.Requested, tenantB);
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    [Fact]
    public async Task AssignDriver_AssignsDriverFromSameTenantOnly()
    {
        var tenantId = Guid.NewGuid();
        SetAdminWithTenant(tenantId);
        var trip = MakeTrip(null, TripStatus.Requested, tenantId);
        var driverId = Guid.NewGuid();
        var otherTenantDriver = new DriverAvailability
        {
            DriverId = driverId,
            Driver = new Driver { Id = driverId, TenantId = Guid.NewGuid() },
            IsOnline = true,
            CurrentTripId = null,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        _tripRepo.Setup(r => r.GetByIdAsync(trip.Id, default)).ReturnsAsync(trip);
        _availRepo.Setup(r => r.GetFirstAvailableAsync(default)).ReturnsAsync(otherTenantDriver);

        var result = await CreateHandler().Handle(new AssignDriverCommand(trip.Id), default);

        Assert.False(result.Succeeded);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }
}

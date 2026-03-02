using Moq;
using Movix.Application.Common.Interfaces;
using Movix.Application.Drivers;
using Movix.Application.Outbox;
using Movix.Application.Pricing;
using Movix.Application.Trips;
using Movix.Application.Trips.Commands.TransitionTrip;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Xunit;

namespace Movix.Application.Tests.Trips;

/// <summary>
/// C-3 regression tests: the tenant used for fare calculation must come exclusively
/// from trip.TenantId — never from a client-supplied source.
/// </summary>
public class TransitionTripCommandHandlerFareTests
{
    private readonly Mock<ITripRepository>               _tripRepo      = new();
    private readonly Mock<ITariffPlanRepository>         _tariffRepo    = new();
    private readonly Mock<IFareCalculator>               _fareCalc      = new();
    private readonly Mock<IDriverAvailabilityRepository> _availRepo     = new();
    private readonly Mock<IOutboxMessageRepository>      _outboxRepo    = new();
    private readonly Mock<ITenantContext>                 _tenantContext = new();
    private readonly Mock<ICurrentUserService>           _currentUser   = new();
    private readonly Mock<IDateTimeService>              _dateTime      = new();
    private readonly Mock<IUnitOfWork>                   _uow           = new();

    private TransitionTripCommandHandler CreateHandler() =>
        new(_tripRepo.Object, _tariffRepo.Object, _fareCalc.Object, _availRepo.Object,
            _outboxRepo.Object, _tenantContext.Object, _currentUser.Object, _dateTime.Object, _uow.Object);

    private Trip MakeTripInProgress(Guid driverId, Guid? tenantId) => new()
    {
        Id             = Guid.NewGuid(),
        PassengerId    = Guid.NewGuid(),
        DriverId       = driverId,
        TenantId       = tenantId,
        Status         = TripStatus.InProgress,
        IdempotencyKey = Guid.NewGuid().ToString(),
        Currency       = "USD",
        CreatedAtUtc   = DateTime.UtcNow,
        UpdatedAtUtc   = DateTime.UtcNow,
        RowVersion     = new byte[] { 1 }
    };

    private static TariffPlan MakePlan(Guid tenantId) => new()
    {
        Id             = Guid.NewGuid(),
        TenantId       = tenantId,
        Name           = "Standard",
        BaseFare       = 5m,
        PricePerKm     = 2m,
        PricePerMinute = 0.5m,
        MinimumFare    = 3m,
        Currency       = "USD",
        IsActive       = true,
        CreatedAtUtc   = DateTime.UtcNow,
        UpdatedAtUtc   = DateTime.UtcNow
    };

    /// <summary>
    /// CompleteTrip uses GetApplicableTariffAsync(tenantId, now) for fare calculation.
    /// </summary>
    [Fact]
    public async Task CompleteTrip_UsesApplicableTariff()
    {
        var driverId = Guid.NewGuid();
        var tripTenantId = Guid.NewGuid();
        var trip = MakeTripInProgress(driverId, tripTenantId);
        var plan = MakePlan(tripTenantId);

        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _tenantContext.Setup(c => c.TenantId).Returns(tripTenantId);
        _currentUser.Setup(c => c.UserId).Returns(driverId);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _tariffRepo.Setup(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), default)).ReturnsAsync(plan);
        _fareCalc.Setup(f => f.Calculate(plan.BaseFare, plan.PricePerKm, plan.PricePerMinute,
            plan.MinimumFare, 10m, 20m)).Returns(35m);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Completed, null, 10m, 20m), default);

        Assert.True(result.Succeeded);
        _tariffRepo.Verify(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _fareCalc.Verify(f => f.Calculate(plan.BaseFare, plan.PricePerKm, plan.PricePerMinute, plan.MinimumFare, 10m, 20m), Times.Once);
    }

    /// <summary>
    /// C-3 / T1 — The tariff repository is queried with trip.TenantId (T1), not any other value.
    /// </summary>
    [Fact]
    public async Task Complete_TariffIsLookedUpByTripTenantId()
    {
        var driverId     = Guid.NewGuid();
        var tripTenantId = Guid.NewGuid();                          // T1
        var trip         = MakeTripInProgress(driverId, tripTenantId);
        var plan         = MakePlan(tripTenantId);

        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _tenantContext.Setup(c => c.TenantId).Returns(tripTenantId); // context also carries T1
        _currentUser.Setup(c => c.UserId).Returns(driverId);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _tariffRepo.Setup(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), default)).ReturnsAsync(plan);
        _fareCalc.Setup(f => f.Calculate(plan.BaseFare, plan.PricePerKm, plan.PricePerMinute,
            plan.MinimumFare, 10m, 20m)).Returns(35m);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Completed, null, 10m, 20m), default);

        Assert.True(result.Succeeded);

        // T1: tariff repo was called exactly once with trip.TenantId and current time
        _tariffRepo.Verify(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// C-3 / T2 — When the X-Tenant-Id header carries an attacker-controlled value (T2) that
    /// differs from trip.TenantId (T1), the tariff repo must NOT be queried with T2.
    /// Only trip.TenantId is used for fare calculation.
    /// </summary>
    [Fact]
    public async Task Complete_TariffIsNeverLookedUpByContextTenantId_WhenTripHasDifferentTenantId()
    {
        var driverId         = Guid.NewGuid();
        var tripTenantId     = Guid.NewGuid();  // T1 — persisted on the trip
        var attackerTenantId = Guid.NewGuid();  // T2 — injected via X-Tenant-Id header
        var trip             = MakeTripInProgress(driverId, tripTenantId);
        var plan             = MakePlan(tripTenantId);

        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        // Context carries the attacker's tenant (T2)
        _tenantContext.Setup(c => c.TenantId).Returns(attackerTenantId);
        _currentUser.Setup(c => c.UserId).Returns(driverId);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);
        _dateTime.Setup(d => d.UtcNow).Returns(DateTime.UtcNow);
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _tariffRepo.Setup(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), default)).ReturnsAsync(plan);
        _fareCalc.Setup(f => f.Calculate(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>())).Returns(35m);

        await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Completed, null, 10m, 20m), default);

        // Must NOT query with the attacker's tenant (T2)
        _tariffRepo.Verify(r => r.GetApplicableTariffAsync(attackerTenantId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        // Must query with the trip's own tenant (T1)
        _tariffRepo.Verify(r => r.GetApplicableTariffAsync(tripTenantId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// C-3 / T3 — When trip.TenantId is null and context carries no tenant either,
    /// the handler returns TENANT_REQUIRED before ever reaching fare calculation.
    /// </summary>
    [Fact]
    public async Task Complete_WhenTripTenantIdNull_AndContextIsNull_Returns_TENANT_REQUIRED_WithoutCallingTariffRepo()
    {
        var driverId = Guid.NewGuid();
        var trip     = MakeTripInProgress(driverId, tenantId: null); // no tenant on trip

        _tripRepo.Setup(r => r.GetByIdWithDriverAsync(trip.Id, default)).ReturnsAsync(trip);
        _tenantContext.Setup(c => c.TenantId).Returns((Guid?)null);   // no header
        _currentUser.Setup(c => c.UserId).Returns(driverId);
        _currentUser.Setup(c => c.Role).Returns(Role.Driver);

        var result = await CreateHandler().Handle(
            new TransitionTripCommand(trip.Id, TripStatus.Completed, null, 10m, 20m), default);

        Assert.False(result.Succeeded);
        Assert.Equal("TENANT_REQUIRED", result.ErrorCode);

        // Tariff repo must never be called
        _tariffRepo.Verify(r => r.GetApplicableTariffAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

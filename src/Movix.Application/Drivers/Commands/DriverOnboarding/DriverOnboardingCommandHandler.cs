using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Drivers.Commands.DriverOnboarding;

public class DriverOnboardingCommandHandler : IRequestHandler<DriverOnboardingCommand, Result<DriverOnboardingResponse>>
{
    private readonly IDriverRepository _driverRepository;
    private readonly IDriverAvailabilityRepository _availabilityRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public DriverOnboardingCommandHandler(
        IDriverRepository driverRepository,
        IDriverAvailabilityRepository availabilityRepository,
        ICurrentUserService currentUser,
        ITenantContext tenantContext,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _driverRepository = driverRepository;
        _availabilityRepository = availabilityRepository;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<DriverOnboardingResponse>> Handle(DriverOnboardingCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result<DriverOnboardingResponse>.Failure("Unauthorized", "UNAUTHORIZED");

        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
            return Result<DriverOnboardingResponse>.Failure("Tenant is required", "TENANT_REQUIRED");

        var existing = await _driverRepository.GetByUserIdAsync(userId.Value, cancellationToken);
        if (existing != null)
            return Result<DriverOnboardingResponse>.Failure("Driver already registered", "DRIVER_EXISTS");

        var now = _dateTime.UtcNow;
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            TenantId = tenantId.Value,
            Status = Movix.Domain.Enums.DriverStatus.Offline,
            IsVerified = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString(),
            RowVersion = new byte[] { 1 }
        };

        if (!string.IsNullOrWhiteSpace(request.VehiclePlate))
        {
            driver.Vehicles.Add(new Vehicle
            {
                Id = Guid.NewGuid(),
                DriverId = driver.Id,
                Plate = request.VehiclePlate,
                Model = request.VehicleModel ?? "",
                Color = request.VehicleColor ?? "",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = userId.ToString(),
                UpdatedBy = userId.ToString(),
                RowVersion = new byte[] { 1 }
            });
        }

        await _driverRepository.AddAsync(driver, cancellationToken);
        await _availabilityRepository.AddAsync(new DriverAvailability
        {
            DriverId = driver.Id,
            IsOnline = false,
            CurrentTripId = null,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 }
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<DriverOnboardingResponse>.Success(new DriverOnboardingResponse(driver.Id, driver.IsVerified));
    }
}

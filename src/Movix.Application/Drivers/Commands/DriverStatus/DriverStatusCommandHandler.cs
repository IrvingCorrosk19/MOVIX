using MediatR;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using Movix.Domain.Enums;

namespace Movix.Application.Drivers.Commands.DriverStatus;

public class DriverStatusCommandHandler : IRequestHandler<DriverStatusCommand, Result>
{
    private readonly IDriverRepository _driverRepository;
    private readonly IDriverAvailabilityRepository _availabilityRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public DriverStatusCommandHandler(
        IDriverRepository driverRepository,
        IDriverAvailabilityRepository availabilityRepository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _driverRepository = driverRepository;
        _availabilityRepository = availabilityRepository;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result> Handle(DriverStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result.Failure("Unauthorized", "UNAUTHORIZED");

        var driver = await _driverRepository.GetByUserIdAsync(userId.Value, cancellationToken);
        if (driver == null)
            return Result.Failure("Driver not found", "DRIVER_NOT_FOUND");

        var now = _dateTime.UtcNow;
        driver.Status = request.Status;
        driver.UpdatedAtUtc = now;
        driver.UpdatedBy = userId.ToString();

        await _driverRepository.UpdateAsync(driver, cancellationToken);

        var availability = await _availabilityRepository.GetByDriverIdAsync(driver.Id, cancellationToken);
        if (availability == null)
        {
            await _availabilityRepository.AddAsync(new DriverAvailability
            {
                DriverId = driver.Id,
                IsOnline = request.Status == Movix.Domain.Enums.DriverStatus.Online,
                CurrentTripId = null,
                UpdatedAtUtc = now,
                RowVersion = new byte[] { 1 }
            }, cancellationToken);
        }
        else
        {
            availability.IsOnline = request.Status == Movix.Domain.Enums.DriverStatus.Online;
            availability.UpdatedAtUtc = now;
        }

        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return Result.Failure("Concurrent modification", "CONFLICT");
        }

        return Result.Success();
    }
}

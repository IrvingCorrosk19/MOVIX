using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;

namespace Movix.Application.Drivers.Commands.DriverStatus;

public class DriverStatusCommandHandler : IRequestHandler<DriverStatusCommand, Result>
{
    private readonly IDriverRepository _driverRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public DriverStatusCommandHandler(
        IDriverRepository driverRepository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _driverRepository = driverRepository;
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

        driver.Status = request.Status;
        driver.UpdatedAtUtc = _dateTime.UtcNow;
        driver.UpdatedBy = userId.ToString();

        await _driverRepository.UpdateAsync(driver, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

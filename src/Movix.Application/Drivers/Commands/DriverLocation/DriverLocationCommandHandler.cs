using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using NetTopologySuite.Geometries;

namespace Movix.Application.Drivers.Commands.DriverLocation;

public class DriverLocationCommandHandler : IRequestHandler<DriverLocationCommand, Result>
{
    private readonly IDriverRepository _driverRepository;
    private readonly IDriverLocationRepository _locationRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public DriverLocationCommandHandler(
        IDriverRepository driverRepository,
        IDriverLocationRepository locationRepository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _driverRepository = driverRepository;
        _locationRepository = locationRepository;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result> Handle(DriverLocationCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
            return Result.Failure("Unauthorized", "UNAUTHORIZED");

        var driver = await _driverRepository.GetByUserIdAsync(userId.Value, cancellationToken);
        if (driver == null)
            return Result.Failure("Driver not found", "DRIVER_NOT_FOUND");

        var now = _dateTime.UtcNow;
        var point = new Point(new NetTopologySuite.Geometries.Coordinate(request.Longitude, request.Latitude)) { SRID = 4326 };

        var location = new DriverLocationLive
        {
            Id = Guid.NewGuid(),
            DriverId = driver.Id,
            Location = point,
            Heading = request.Heading,
            Speed = request.Speed,
            RecordedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId.ToString(),
            UpdatedBy = userId.ToString()
        };

        await _locationRepository.AddAsync(location, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

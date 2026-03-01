using Movix.Application.Common.Interfaces;

namespace Movix.Infrastructure.Services;

public class DateTimeService : IDateTimeService
{
    public DateTime UtcNow => DateTime.UtcNow;
}

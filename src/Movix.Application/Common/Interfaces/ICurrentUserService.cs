using Movix.Domain.Enums;

namespace Movix.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    Role? Role { get; }
    bool IsAuthenticated { get; }
}

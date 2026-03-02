using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Domain.Entities;
using Movix.Application.Tenants;

namespace Movix.Application.Tenants.Commands.CreateTenant;

public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Result<TenantDto>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public CreateTenantCommandHandler(ITenantRepository tenantRepository, IDateTimeService dateTime, IUnitOfWork uow)
    {
        _tenantRepository = tenantRepository;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<TenantDto>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var now = _dateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[] { 1 }
        };
        await _tenantRepository.AddAsync(tenant, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<TenantDto>.Success(new TenantDto(tenant.Id, tenant.Name, tenant.IsActive, tenant.CreatedAtUtc));
    }
}

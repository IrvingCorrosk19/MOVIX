using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing;
using Movix.Domain.Entities;

namespace Movix.Application.Pricing.Commands.CreateTariffPlan;

public class CreateTariffPlanCommandHandler : IRequestHandler<CreateTariffPlanCommand, Result<TariffPlanDto>>
{
    private readonly ITariffPlanRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public CreateTariffPlanCommandHandler(
        ITariffPlanRepository repository,
        ICurrentUserService currentUser,
        IDateTimeService dateTime,
        IUnitOfWork uow)
    {
        _repository = repository;
        _currentUser = currentUser;
        _dateTime = dateTime;
        _uow = uow;
    }

    public async Task<Result<TariffPlanDto>> Handle(CreateTariffPlanCommand request, CancellationToken cancellationToken)
    {
        var now = _dateTime.UtcNow;
        var by = _currentUser.UserId?.ToString() ?? "system";
        var plan = new TariffPlan
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Name = request.Name,
            Currency = request.Currency,
            BaseFare = request.BaseFare,
            PricePerKm = request.PricePerKm,
            PricePerMinute = request.PricePerMinute,
            MinimumFare = request.MinimumFare,
            IsActive = false,
            Priority = request.Priority,
            EffectiveFromUtc = request.EffectiveFromUtc,
            EffectiveUntilUtc = request.EffectiveUntilUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = by,
            UpdatedBy = by
        };
        await _repository.AddAsync(plan, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<TariffPlanDto>.Success(Map(plan));
    }

    private static TariffPlanDto Map(TariffPlan p) => new(
        p.Id, p.TenantId, p.Name, p.Currency, p.BaseFare, p.PricePerKm, p.PricePerMinute, p.MinimumFare,
        p.IsActive, p.Priority, p.EffectiveFromUtc, p.EffectiveUntilUtc, p.CreatedAtUtc);
}

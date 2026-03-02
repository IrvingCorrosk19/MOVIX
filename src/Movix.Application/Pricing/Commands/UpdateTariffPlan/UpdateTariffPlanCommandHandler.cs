using MediatR;
using Movix.Application.Common.Interfaces;
using Movix.Application.Common.Models;
using Movix.Application.Pricing.Commands.CreateTariffPlan;

namespace Movix.Application.Pricing.Commands.UpdateTariffPlan;

public class UpdateTariffPlanCommandHandler : IRequestHandler<UpdateTariffPlanCommand, Result<TariffPlanDto>>
{
    private readonly ITariffPlanRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeService _dateTime;
    private readonly IUnitOfWork _uow;

    public UpdateTariffPlanCommandHandler(
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

    public async Task<Result<TariffPlanDto>> Handle(UpdateTariffPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _repository.GetByIdAsync(request.TariffPlanId, cancellationToken);
        if (plan == null)
            return Result<TariffPlanDto>.Failure("Tariff plan not found", "TARIFF_NOT_FOUND");
        if (plan.TenantId != request.TenantId)
            return Result<TariffPlanDto>.Failure("Tariff plan does not belong to tenant", "TARIFF_CROSS_TENANT");

        plan.Name = request.Name;
        plan.Currency = request.Currency;
        plan.BaseFare = request.BaseFare;
        plan.PricePerKm = request.PricePerKm;
        plan.PricePerMinute = request.PricePerMinute;
        plan.MinimumFare = request.MinimumFare;
        plan.EffectiveFromUtc = request.EffectiveFromUtc;
        plan.UpdatedAtUtc = _dateTime.UtcNow;
        plan.UpdatedBy = _currentUser.UserId?.ToString() ?? "system";

        await _uow.SaveChangesAsync(cancellationToken);
        return Result<TariffPlanDto>.Success(Map(plan));
    }

    private static TariffPlanDto Map(Domain.Entities.TariffPlan p) => new(
        p.Id, p.TenantId, p.Name, p.Currency, p.BaseFare, p.PricePerKm, p.PricePerMinute, p.MinimumFare,
        p.IsActive, p.Priority, p.EffectiveFromUtc, p.EffectiveUntilUtc, p.CreatedAtUtc);
}

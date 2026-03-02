using MediatR;
using Movix.Application.Common.Exceptions;
using Movix.Application.Common.Interfaces;
using Movix.Application.Outbox;

namespace Movix.Application.Admin.Commands.ReprocessDeadLetter;

public class ReprocessDeadLetterCommandHandler : IRequestHandler<ReprocessDeadLetterCommand>
{
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IUnitOfWork _uow;

    public ReprocessDeadLetterCommandHandler(IOutboxMessageRepository outboxRepository, IUnitOfWork uow)
    {
        _outboxRepository = outboxRepository;
        _uow = uow;
    }

    public async Task Handle(ReprocessDeadLetterCommand request, CancellationToken cancellationToken)
    {
        var message = await _outboxRepository.GetByIdAsync(request.OutboxMessageId, cancellationToken);
        if (message == null)
            throw new NotFoundException($"Outbox message {request.OutboxMessageId} not found.");
        if (!message.IsDeadLetter)
            throw new InvalidOperationException("Only dead-letter messages can be reprocessed.");

        message.ResetForReprocess();
        await _uow.SaveChangesAsync(cancellationToken);
    }
}

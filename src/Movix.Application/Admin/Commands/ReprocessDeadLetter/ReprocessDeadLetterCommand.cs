using MediatR;

namespace Movix.Application.Admin.Commands.ReprocessDeadLetter;

public record ReprocessDeadLetterCommand(Guid OutboxMessageId) : IRequest;

using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Movix.Application.Admin.Commands.ReprocessDeadLetter;
using Movix.Application.Payments;
using Movix.Domain.Entities;
using Movix.Domain.Enums;
using Movix.Infrastructure.Persistence;
using Movix.Api.Controllers.V1;
using Xunit;

namespace Movix.Api.Tests;

public class AdminOpsControllerTests
{
    private static AdminOpsController CreateController(
        IPaymentRepository? paymentRepo = null,
        MovixDbContext? db = null,
        IMediator? mediator = null)
    {
        var payments = paymentRepo ?? new Mock<IPaymentRepository>().Object;
        var context = db ?? CreateInMemoryContext();
        var mediatR = mediator ?? new Mock<IMediator>().Object;
        var controller = new AdminOpsController(payments, context, mediatR)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"))
                }
            }
        };
        return controller;
    }

    private static MovixDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<MovixDbContext>()
            .UseInMemoryDatabase("ops_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new MovixDbContext(options);
    }

    [Fact]
    public async Task GetPayments_ReturnsFilteredByStatus()
    {
        var tenantId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            PayerId = Guid.NewGuid(),
            Amount = 15m,
            Currency = "USD",
            Status = PaymentStatus.Completed,
            ExternalPaymentId = "pi_1",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = "k",
            RowVersion = new byte[] { 1 },
            Trip = new Trip { Id = tripId, TenantId = tenantId, PassengerId = Guid.NewGuid(), Status = TripStatus.Completed, IdempotencyKey = "tk", Currency = "USD", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, RowVersion = new byte[] { 1 } }
        };
        var mockRepo = new Mock<IPaymentRepository>();
        mockRepo.Setup(r => r.GetFilteredAsync(null, PaymentStatus.Completed, null, null, null, default))
            .ReturnsAsync(new List<Payment> { payment });
        var controller = CreateController(paymentRepo: mockRepo.Object);

        var result = await controller.GetPayments(tenantId: null, status: "Completed", from: null, to: null, tripId: null, ct: default);

        var ok = result as OkObjectResult;
        Assert.NotNull(ok);
        var list = ok!.Value as IEnumerable<object>;
        Assert.NotNull(list);
        Assert.Single(list!);
        mockRepo.Verify(r => r.GetFilteredAsync(null, PaymentStatus.Completed, null, null, null, default), Times.Once);
    }

    [Fact]
    public async Task GetOutbox_ReturnsDeadLetters()
    {
        var db = CreateInMemoryContext();
        var deadMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "PaymentFailed",
            Payload = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        deadMessage.MarkAsDeadLetter();
        db.OutboxMessages.Add(deadMessage);
        await db.SaveChangesAsync();
        var controller = CreateController(db: db);

        var result = await controller.GetOutbox(processed: null, deadletter: true, from: null, to: null, type: null, ct: default);

        var ok = result as OkObjectResult;
        Assert.NotNull(ok);
        var list = ok!.Value as IEnumerable<object>;
        Assert.NotNull(list);
        Assert.Single(list!);
    }

    [Fact]
    public async Task Reprocess_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        var mockMediator = new Mock<IMediator>();
        mockMediator.Setup(m => m.Send(It.Is<ReprocessDeadLetterCommand>(c => c.OutboxMessageId == id), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(mediator: mockMediator.Object);

        var result = await controller.Reprocess(id, default);

        var noContent = result as NoContentResult;
        Assert.NotNull(noContent);
        mockMediator.Verify(m => m.Send(It.Is<ReprocessDeadLetterCommand>(c => c.OutboxMessageId == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}

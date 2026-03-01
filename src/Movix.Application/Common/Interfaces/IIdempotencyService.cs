namespace Movix.Application.Common.Interfaces;

public interface IIdempotencyService
{
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task StoreAsync(string key, string responsePayload, CancellationToken cancellationToken = default);
    Task<string?> GetResponseAsync(string key, CancellationToken cancellationToken = default);
}

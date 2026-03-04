namespace Movix.Application.Common.Exceptions;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException()
        : base("The record was modified concurrently by another operation.") { }

    /// <summary>Preserve inner DbUpdateConcurrencyException for diagnostics (failed entries).</summary>
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}

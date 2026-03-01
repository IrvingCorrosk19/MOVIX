namespace Movix.Application.Common.Exceptions;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException()
        : base("The record was modified concurrently by another operation.") { }
}

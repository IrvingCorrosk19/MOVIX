namespace Movix.Application.Common.Models;

public class Result
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static Result Success() => new() { Succeeded = true };
    public static Result Failure(string error, string? errorCode = null) => new() { Succeeded = false, Error = error, ErrorCode = errorCode };
}

public class Result<T> : Result
{
    public T? Data { get; init; }

    public static Result<T> Success(T data) => new() { Succeeded = true, Data = data };
    public new static Result<T> Failure(string error, string? errorCode = null) => new() { Succeeded = false, Error = error, ErrorCode = errorCode };
}

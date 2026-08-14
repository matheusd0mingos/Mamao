namespace Mamao.SharedKernel.Results;

/// <summary>
/// Falha de regra de negocio esperada. Nao usar para bug nem para falha de
/// infraestrutura — essas continuam sendo excecao.
/// </summary>
/// <param name="Code">Estavel, consumido pelo frontend. Ex.: "employee.duplicate_code".</param>
/// <param name="Message">Texto pt-BR para o usuario.</param>
/// <param name="Field">Campo do formulario, quando a falha for de um campo especifico.</param>
public sealed record Error(string Code, string Message, string? Field = null);

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess == (error is not null))
            throw new ArgumentException("Resultado com sucesso e erro inconsistentes.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
    public static Result Failure(string code, string message, string? field = null)
        => new(false, new Error(code, message, field));

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error) : base(isSuccess, error)
        => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Resultado com falha nao tem valor.");

    internal static Result<T> Ok(T value) => new(true, value, null);
    internal static Result<T> Fail(Error error) => new(false, default, error);
}

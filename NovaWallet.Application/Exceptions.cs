namespace NovaWallet.Application.Exceptions;

public class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
public sealed class NotFoundAppException(string message) : AppException(message, 404, "not_found");
public sealed class ValidationAppException(string message) : AppException(message, 400, "validation_error");
public sealed class ConflictAppException(string message) : AppException(message, 409, "conflict");
public sealed class UnauthorizedAppException(string message) : AppException(message, 401, "unauthorized");

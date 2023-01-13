namespace ShopStream.BuildingBlocks.Errors;

public abstract class AppException : Exception
{
    protected AppException(string message, int statusCode, string title) : base(message)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message, 404, "Resource not found") { }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base(message, 409, "Conflict") { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message, 403, "Forbidden") { }
}

public sealed class BadRequestException : AppException
{
    public BadRequestException(string message) : base(message, 400, "Bad request") { }
}

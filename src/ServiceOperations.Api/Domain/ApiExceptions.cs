namespace ServiceOperations.Api.Domain;

public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ResourceNotFoundException(string message) : Exception(message);

public sealed class ResourceConflictException(string message) : Exception(message);


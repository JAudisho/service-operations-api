using ServiceOperations.Api.Contracts;
using ServiceOperations.Api.Domain;

namespace ServiceOperations.Api.Validation;

public static class RequestValidation
{
    public static void Validate(CreateServiceRequest request)
    {
        var errors = NewErrors();
        Required(errors, nameof(request.Name), request.Name, 120);
        MaxLength(errors, nameof(request.Description), request.Description, 500);
        Defined(errors, nameof(request.InitialStatus), request.InitialStatus);
        ThrowIfAny(errors);
    }

    public static void Validate(UpdateServiceRequest request)
    {
        var errors = NewErrors();
        Required(errors, nameof(request.Name), request.Name, 120);
        MaxLength(errors, nameof(request.Description), request.Description, 500);
        ThrowIfAny(errors);
    }

    public static void Validate(ChangeServiceStatusRequest request)
    {
        var errors = NewErrors();
        MaxLength(errors, nameof(request.Message), request.Message, 500);
        Defined(errors, nameof(request.Status), request.Status);
        ThrowIfAny(errors);
    }

    public static void Validate(CreateIncidentRequest request)
    {
        var errors = NewErrors();
        if (request.ServiceId < 1) Add(errors, nameof(request.ServiceId), "ServiceId must be positive.");
        Required(errors, nameof(request.Title), request.Title, 160);
        Required(errors, nameof(request.Description), request.Description, 4000);
        Defined(errors, nameof(request.Severity), request.Severity);
        if (request.StartedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            Add(errors, nameof(request.StartedAtUtc), "StartedAtUtc cannot be in the future.");
        ThrowIfAny(errors);
    }

    public static void Validate(UpdateIncidentRequest request)
    {
        var errors = NewErrors();
        Required(errors, nameof(request.Title), request.Title, 160);
        Required(errors, nameof(request.Description), request.Description, 4000);
        Defined(errors, nameof(request.Severity), request.Severity);
        Defined(errors, nameof(request.Status), request.Status);
        ThrowIfAny(errors);
    }

    public static void Validate(AddResolutionNoteRequest request)
    {
        var errors = NewErrors();
        Required(errors, nameof(request.Note), request.Note, 2000);
        Required(errors, nameof(request.CreatedBy), request.CreatedBy, 120);
        ThrowIfAny(errors);
    }

    public static void Validate(ResolveIncidentRequest request)
    {
        var errors = NewErrors();
        Required(errors, nameof(request.ResolutionSummary), request.ResolutionSummary, 2000);
        Required(errors, nameof(request.ResolvedBy), request.ResolvedBy, 120);
        ThrowIfAny(errors);
    }

    private static Dictionary<string, List<string>> NewErrors() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static void Required(
        IDictionary<string, List<string>> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(errors, field, $"{field} is required.");
        else if (value.Trim().Length > maxLength)
            Add(errors, field, $"{field} must be {maxLength} characters or fewer.");
    }

    private static void MaxLength(
        IDictionary<string, List<string>> errors, string field, string? value, int maxLength)
    {
        if (value?.Trim().Length > maxLength)
            Add(errors, field, $"{field} must be {maxLength} characters or fewer.");
    }

    private static void Add(IDictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
            errors[field] = messages = [];
        messages.Add(message);
    }

    private static void Defined<TEnum>(
        IDictionary<string, List<string>> errors, string field, TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) Add(errors, field, $"{field} is invalid.");
    }

    private static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
            throw new RequestValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray()));
    }
}

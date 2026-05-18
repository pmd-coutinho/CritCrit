using Microsoft.AspNetCore.Mvc;

namespace CritCrit.Api.Org.Endpoints;

// Shared shape-validation helpers used by Wolverine `Validate` convention
// methods on endpoint classes. Each helper yields zero or more ProblemDetails;
// callers compose them with LINQ Concat so a single request returns all field
// failures together. Replaces FluentValidation AbstractValidator classes —
// see .scratch/validator-removal/.
public static class OrgValidators
{
    public static ProblemDetails ValidatePlainOrgNode(CreatePlainOrgNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParentId))
            return new ProblemDetails { Title = "parentId", Detail = "parentId is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Code))
            return new ProblemDetails { Title = "code", Detail = "code is required.", Status = 400 };
        if (request.Code.Length > 128)
            return new ProblemDetails { Title = "code", Detail = "code must be 128 characters or fewer.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Name))
            return new ProblemDetails { Title = "name", Detail = "name is required.", Status = 400 };
        if (request.Name.Length > 200)
            return new ProblemDetails { Title = "name", Detail = "name must be 200 characters or fewer.", Status = 400 };
        return Wolverine.Http.WolverineContinue.NoProblems;
    }

    public static IEnumerable<ProblemDetails> RequiredString(string field, string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return new ProblemDetails { Title = field, Detail = $"{field} is required.", Status = 400 };
            yield break;
        }
        if (maxLength is { } max && value.Length > max)
            yield return new ProblemDetails { Title = field, Detail = $"{field} must be {max} characters or fewer.", Status = 400 };
    }

    public static IEnumerable<ProblemDetails> MaxLength(string field, string? value, int max)
    {
        if (value is { } v && v.Length > max)
            yield return new ProblemDetails { Title = field, Detail = $"{field} must be {max} characters or fewer.", Status = 400 };
    }

    public static IEnumerable<ProblemDetails> RequiredId(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield return new ProblemDetails { Title = field, Detail = $"{field} is required.", Status = 400 };
    }

    public static IEnumerable<ProblemDetails> Email(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return new ProblemDetails { Title = field, Detail = $"{field} is required.", Status = 400 };
            yield break;
        }
        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1 || value.IndexOf('.', at) < 0)
            yield return new ProblemDetails { Title = field, Detail = $"{field} must be a valid email.", Status = 400 };
    }

    public static IEnumerable<ProblemDetails> EnumDefined<TEnum>(string field, TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            yield return new ProblemDetails { Title = field, Detail = $"{field} is not a recognised value.", Status = 400 };
    }

    public static IEnumerable<ProblemDetails> FutureUtc(string field, DateTimeOffset? value)
    {
        if (value is { } at && at <= DateTimeOffset.UtcNow)
            yield return new ProblemDetails { Title = field, Detail = $"{field} must be in the future.", Status = 400 };
    }
}

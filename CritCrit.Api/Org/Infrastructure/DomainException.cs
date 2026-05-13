namespace CritCrit.Api.Org.Infrastructure;

public sealed class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

namespace CritCrit.Api2.Infrastructure.Email;

public abstract class EmailMessage
{
    public Guid Id { get; set; }
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
}
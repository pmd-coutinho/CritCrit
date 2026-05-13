using CritCrit.Api.Org.Auth;

namespace CritCrit.Api.Org.Infrastructure;

public static class HttpContextActorExtensions
{
    public static ActorContext GetActor(this HttpContext httpContext) =>
        httpContext.Items[OrgActorMiddleware.ItemKey] as ActorContext
        ?? throw new DomainException("Actor not resolved.");
}

using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Http;

namespace CritCrit.Api.Org.Infrastructure;

public class OrgHttpPolicy : IHttpPolicy
{
    public void Apply(IReadOnlyList<HttpChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            var pattern = chain.RoutePattern?.RawText;
            if (pattern is null)
                continue;

            if (!pattern.StartsWith("/api/brands/", StringComparison.OrdinalIgnoreCase) &&
                !pattern.StartsWith("/api/platform/", StringComparison.OrdinalIgnoreCase))
                continue;

            chain.Middleware.Insert(0, new MethodCall(
                typeof(OrgActorMiddleware),
                nameof(OrgActorMiddleware.Apply)));
        }
    }
}

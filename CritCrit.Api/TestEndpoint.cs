using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace CritCrit.Api;

public static class TestEndpoint
{
    [Authorize(Roles = "admin")]
    [WolverineGet("/api/test")]
    public static IResult Get()
    {
        return Results.Ok("test");
    }
}
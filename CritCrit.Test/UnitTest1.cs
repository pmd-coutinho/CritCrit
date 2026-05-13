using System.Net;
using Alba;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public class UnitTest1(ApiFixture fixture) : ContractTestWithAlba(fixture)
{
    [Fact]
    public Task Test1()
    {
        return Host.Scenario(_ =>
        {
            _.Get.Url("/api/test");
            _.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }
    
    [Fact]
    public Task Test2()
    {
        return Host.Scenario(_ =>
        {
            _.Get.Url("/api/testssss");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public Task Create_brand_route_is_routed()
    {
        return Host.Scenario(_ =>
        {
            _.Post.Json(new { code = "abc", name = "ABC" }, JsonStyle.MinimalApi).ToUrl("/api/platform/brands");
            _.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }
}

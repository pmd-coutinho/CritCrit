using Alba;

namespace CritCrit.AlbaTests.Fixtures;

public class ContractTestWithAlba(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    protected readonly IAlbaHost Host = fixture.AlbaHost;
    
    
}
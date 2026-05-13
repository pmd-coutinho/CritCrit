using Alba;
using Microsoft.Extensions.Hosting;

namespace CritCrit.Test.Fixtures;

public class ContractTestWithAlba(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    protected readonly IAlbaHost Host = fixture.AlbaHost;
    
    
}
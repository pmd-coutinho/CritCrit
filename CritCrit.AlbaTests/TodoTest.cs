using Alba;
using CritCrit.AlbaTests.Fixtures;
using CritCrit.Api2.Infrastructure.Email;
using CritCrit.Api2.Todo.Domain;
using CritCrit.Api2.Todo.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine;

namespace CritCrit.AlbaTests;

public class TodoTest(ApiFixture fixture) : IntegrationContext(fixture)
{
    [Fact]
    public async Task should_create_todo()
    {
        var emailStore = Host.Services.GetRequiredService<TestEmailStore>();
        var originalCount = emailStore.Sent.Count;
        var (tracked, results) = await TrackedHttpCall( _ =>
        {
            _.WithRequestHeader("X-Tenant-ID", "xxxx");
            _.Post.Json(new TodoEndpoints.CreateTodoRequest("Buy groceries", "test"), JsonStyle.MinimalApi)
                .ToUrl("/api/todos");
            _.StatusCodeShouldBe(201);
        });
        
        string url = results.Context.Response.Headers.Location!; 

        // Follow up: fetch the created resource
        var result = await Host.Scenario(_ =>
        {
            _.WithRequestHeader("X-Tenant-ID", "xxxx");
            _.Get.Url(url);
        });

        var todo = await result.ReadAsJsonAsync<Todo>();
        todo!.Name.ShouldBe("Buy groceries");

        tracked.Executed.SingleMessage<TodoCreatedEmail>().WithTenantId("xxxx");

        emailStore.Sent.Count.ShouldBeGreaterThan(originalCount);
    }
}
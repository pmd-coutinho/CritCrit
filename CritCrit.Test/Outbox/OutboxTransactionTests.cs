using CritCrit.Test.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace CritCrit.Test.Outbox;

public sealed class OutboxTransactionTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task enrolled_outbox_message_is_delivered_after_marten_commit()
    {
        var id = Guid.CreateVersion7();
        var store = Host.Services.GetRequiredService<OutboxProbeStore>();

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            await Bus.InvokeAsync(new CommitOutboxProbe(id));
        });

        tracked.Executed.SingleMessage<OutboxProbeDelivered>();
        Assert.Contains(id, store.Delivered);
    }

    [Fact]
    public async Task enrolled_outbox_message_is_not_delivered_when_marten_session_is_not_committed()
    {
        var id = Guid.CreateVersion7();
        var store = Host.Services.GetRequiredService<OutboxProbeStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Bus.InvokeAsync(new RollbackOutboxProbe(id));
        });

        await Task.Delay(250);

        Assert.DoesNotContain(id, store.Delivered);
    }
}

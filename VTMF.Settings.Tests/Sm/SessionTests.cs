using VTMF.Settings.Core.Sm;
using VTMF.Settings.Tests.Sql;

namespace VTMF.Settings.Tests.Sm;

[Trait("Category", "Integration")]
[Collection(SqlCollection.Name)]
public sealed class SessionTests(SqlServerFixture fixture)
{
    private const string Client = SqlServerFixture.PlanClientName;

    [Fact]
    public async Task StartSession_ReturnsOpenSession()
    {
        var process = await PlanSandbox.CreateProcessAsync(fixture, Client);

        var session = await fixture.Sm.StartSessionAsync(Client, "LOT column", process);

        Assert.Equal("Open", session.Status);
        Assert.Equal("LOT column", session.Name);
        Assert.Equal(process, session.Process);
        Assert.False(string.IsNullOrEmpty(session.CreatedBy));
        Assert.Equal(0, session.PlanCount);
        Assert.Equal(0, session.AppliedCount);
    }

    [Fact]
    public async Task GetSessions_NewestFirst_FilteredByProcess()
    {
        var p1 = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var p2 = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var first = await fixture.Sm.StartSessionAsync(Client, "one", p1);
        var second = await fixture.Sm.StartSessionAsync(Client, "two", p1);
        await fixture.Sm.StartSessionAsync(Client, "other", p2);

        var sessions = await fixture.Sm.GetSessionsAsync(Client, p1);

        Assert.Equal([second.Id, first.Id], sessions.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task CloseSession_SetsClosed()
    {
        var process = await PlanSandbox.CreateProcessAsync(fixture, Client);
        var session = await fixture.Sm.StartSessionAsync(Client, "to close", process);

        var closed = await fixture.Sm.CloseSessionAsync(Client, session.Id);
        var again = await fixture.Sm.CloseSessionAsync(Client, session.Id);

        Assert.Equal("Closed", closed.Status);
        Assert.Equal("Closed", again.Status);
        Assert.Equal(closed.LastActivity, again.LastActivity);
    }

    [Fact]
    public async Task CloseSession_Unknown_ThrowsSessionNotFound()
    {
        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.CloseSessionAsync(Client, int.MaxValue));

        Assert.Equal(SmErrorCodes.SessionNotFound, ex.Code);
    }

    [Fact]
    public async Task StartSession_BlankName_ThrowsInvalidDesired()
    {
        var process = await PlanSandbox.CreateProcessAsync(fixture, Client);

        var ex = await Assert.ThrowsAsync<SmException>(() => fixture.Sm.StartSessionAsync(Client, "  ", process));

        Assert.Equal(SmErrorCodes.InvalidDesired, ex.Code);
    }
}

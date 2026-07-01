using App.Application.Abstractions;
using App.Application.Expressions;
using App.Application.Sync;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Remote;
using App.Infrastructure.Remote.Formats;
using App.Web.Workspaces;

namespace App.E2E.Tests;

/// <summary>
/// The web host's per-user workspace model: each user gets an isolated working copy and a host-namespaced
/// writer id; published edits flow through the shared folder to other users/hosts, while unpublished
/// edits stay private. Plain unit test (no browser) — always runs.
/// </summary>
public sealed class PerUserWorkspaceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _remoteFolder;
    private readonly List<WorkspaceRegistry> _registries = [];

    public PerUserWorkspaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-ws-" + Guid.NewGuid().ToString("N"));
        _remoteFolder = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(_remoteFolder);
    }

    // A registry for one host, all hosts pointing at the same shared folder.
    private WorkspaceRegistry Host(string host)
    {
        FunctionLibrary functions = new();
        CsvRemoteFormat format = new();
        WorkspaceDependencies deps = new(
            new SystemClock(),
            new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)),
            new AutoRefreshPlanner(),
            new FieldMergeEngine(),
            new FileRemoteStore(_remoteFolder, format),
            new SnapshotBuilder(_remoteFolder, format));

        WorkspaceRegistry registry = new(Path.Combine(_dir, host, "users"), host, deps);
        _registries.Add(registry);
        return registry;
    }

    private static Row App(string code) => new(TableNames.Application, Guid.NewGuid()) { ["app_code"] = code };

    [Fact]
    public void Each_user_gets_an_isolated_workspace_with_a_host_namespaced_writer_id()
    {
        WorkspaceRegistry hostA = Host("hostA");

        Workspace alice = hostA.Get(@"CONTOSO\alice");
        Workspace bob = hostA.Get("bob");

        Assert.Equal("CONTOSO_alice@hostA", alice.WriterId); // sanitized + host-namespaced (model B)
        Assert.Equal("bob@hostA", bob.WriterId);
        Assert.NotSame(alice.Store, bob.Store);
        Assert.Same(alice, hostA.Get(@"CONTOSO\alice")); // cached per user
        Assert.Equal(2, hostA.Active.Count);
    }

    [Fact]
    public void Published_edits_reach_other_users_but_unpublished_edits_stay_private()
    {
        WorkspaceRegistry hostA = Host("hostA");
        Workspace alice = hostA.Get("alice");
        Workspace bob = hostA.Get("bob");

        // Alice creates + publishes an application.
        alice.Store.Upsert(TableNames.Application, App("ALICE1"), "cs", "alice");
        Assert.True(alice.Coordinator.Publish([]).Published);

        // Bob, after a refresh, sees Alice's published row...
        bob.Coordinator.Refresh();
        Assert.Contains(bob.Store.GetAll(TableNames.Application), r => r["app_code"] == "ALICE1");

        // ...but Bob's unpublished edit is not visible to Alice.
        bob.Store.Upsert(TableNames.Application, App("BOB_DRAFT"), "cs", "bob");
        alice.Coordinator.Refresh();
        Assert.DoesNotContain(alice.Store.GetAll(TableNames.Application), r => r["app_code"] == "BOB_DRAFT");
    }

    [Fact]
    public void Two_hosts_serving_the_same_user_merge_through_the_shared_folder()
    {
        Workspace onA = Host("hostA").Get("alice");
        Workspace onB = Host("hostB").Get("alice");

        Assert.Equal("alice@hostA", onA.WriterId);
        Assert.Equal("alice@hostB", onB.WriterId); // distinct per-writer logs -> no cross-host collision

        onB.Store.Upsert(TableNames.Application, App("FROM_B"), "cs", "alice");
        Assert.True(onB.Coordinator.Publish([]).Published);

        onA.Coordinator.Refresh();
        Assert.Contains(onA.Store.GetAll(TableNames.Application), r => r["app_code"] == "FROM_B");
    }

    [Fact]
    public void The_accessor_resolves_an_isolated_workspace_for_the_current_user()
    {
        WorkspaceRegistry hostA = Host("hostA");

        // The same wiring the host uses: the scoped accessor maps ICurrentUser.Name -> the user's workspace.
        Workspace forAlice = new WorkspaceAccessor(hostA, new FakeUser(@"CONTOSO\alice")).Current;
        Workspace forBob = new WorkspaceAccessor(hostA, new FakeUser("bob")).Current;

        Assert.Equal("CONTOSO_alice@hostA", forAlice.WriterId);
        Assert.Equal("bob@hostA", forBob.WriterId);
        Assert.NotSame(forAlice.Store, forBob.Store);
    }

    [Fact]
    public void Sweep_evicts_idle_workspaces_but_never_one_with_a_live_circuit()
    {
        WorkspaceRegistry hostA = Host("hostA");
        Workspace alice = hostA.Get("alice");
        Workspace bob = hostA.Get("bob");
        alice.LastAccessUtc = bob.LastAccessUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        // Alice has a live circuit; bob does not. Bob is evicted; Alice is preserved (her connection stays
        // open, so her open circuit's cached store keeps working).
        hostA.SweepIdle(TimeSpan.FromMinutes(30), key => key == "alice");

        Assert.Single(hostA.Active);
        Assert.Same(alice, hostA.Get("alice"));
        Assert.DoesNotContain(bob, hostA.Active);
    }

    [Fact]
    public void Liveness_is_active_while_any_circuit_is_open_and_clears_when_all_close()
    {
        WorkspaceLiveness liveness = new();
        Assert.False(liveness.IsActive("alice"));

        liveness.Enter("alice");
        liveness.Enter("alice"); // two tabs / circuits
        Assert.True(liveness.IsActive("alice"));

        liveness.Leave("alice");
        Assert.True(liveness.IsActive("alice")); // one circuit still open

        liveness.Leave("alice");
        Assert.False(liveness.IsActive("alice"));
    }

    private sealed class FakeUser(string name) : ICurrentUser
    {
        public string UserId => name;
        public string Name => name;
        public string DisplayName => name;
        public IReadOnlyCollection<string> Roles => ["Administrator"];
        public bool HasPermission(string permission) => true;
    }

    public void Dispose()
    {
        foreach (WorkspaceRegistry registry in _registries)
        {
            registry.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}

using App.Application.Sync;
using App.Domain.Data;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

/// <summary>Configurable fake coordinator for publish/conflict UI tests.</summary>
public sealed class FakeSyncCoordinator : ISyncCoordinator
{
    public string WriterId => "test";
    public RemoteHealth RemoteStatus { get; set; } = RemoteHealth.Unknown;
    public int Pending { get; set; }
    public MergeResult PreviewResult { get; set; } = new([], []);
    public PublishResult PublishResult { get; set; } = new(true, 0, []);
    public List<IReadOnlyList<ConflictResolution>> PublishCalls { get; } = [];

    public int PendingCount() => Pending;
    public MergeResult Preview() => PreviewResult;
    public PublishResult Publish(IReadOnlyList<ConflictResolution> resolutions)
    {
        PublishCalls.Add(resolutions);
        return PublishResult;
    }
    public RefreshResult Refresh() => new(2, 1);
    public IReadOnlyList<ChangeLogEntry> History(string? table = null, Guid? rowId = null) => [];
}

public class PublishTests : AppTestContext
{
    private FakeSyncCoordinator _sync = null!;

    private IRenderedComponent<PublishPanel> RenderPanel()
    {
        _sync = new FakeSyncCoordinator();
        Services.AddSingleton<ISyncCoordinator>(_sync);
        Services.AddSingleton<LanguageState>();
        return Render<PublishPanel>();
    }

    private static readonly CellKey Cell = new("data_source", Guid.NewGuid(), "name");

    [Fact]
    public void Clean_publish_reports_the_change_count()
    {
        var cut = RenderPanel();
        _sync.PublishResult = new PublishResult(true, 3, []);

        cut.Find("button.ms-publish").Click(); // Publish

        Assert.Contains("Published 3 change(s).", cut.Markup);
        Assert.Single(_sync.PublishCalls);
    }

    [Fact]
    public void Conflicts_show_the_resolution_dialog_then_publish()
    {
        var cut = RenderPanel();
        _sync.PreviewResult = new MergeResult([], [new MergeConflict(Cell, "base", "mine", "theirs")]);
        _sync.PublishResult = new PublishResult(true, 1, []);

        cut.Find("button.ms-publish").Click(); // Publish → conflicts
        Assert.Contains("name", cut.Markup);
        Assert.Contains("theirs", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent.Contains("Resolve", StringComparison.Ordinal)).Click();

        IReadOnlyList<ConflictResolution> sent = Assert.Single(_sync.PublishCalls);
        Assert.Equal(Cell, Assert.Single(sent).Cell);
        Assert.Contains("Published 1 change(s).", cut.Markup);
    }

    [Fact]
    public void Refresh_reports_applied_and_flagged()
    {
        var cut = RenderPanel();

        cut.FindAll("button").First(b => b.TextContent.Contains("Refresh", StringComparison.Ordinal)).Click();

        Assert.Contains("2 applied", cut.Markup);
        Assert.Contains("1 flagged", cut.Markup);
    }

    [Fact]
    public void Unavailable_remote_shows_a_banner_with_the_reason()
    {
        _sync = new FakeSyncCoordinator { RemoteStatus = RemoteHealth.Down("offline", null) };
        Services.AddSingleton<ISyncCoordinator>(_sync);
        Services.AddSingleton<LanguageState>();
        var cut = Render<PublishPanel>();

        Assert.Contains("Shared folder unavailable", cut.Markup);
        Assert.Contains("offline", cut.Markup);
    }

    [Fact]
    public void Conflict_dialog_emits_keep_theirs_resolution()
    {
        IReadOnlyList<ConflictResolution>? captured = null;
        Services.AddSingleton<LanguageState>();
        var cut = Render<ConflictDialog>(p => p
            .Add(c => c.Conflicts, [new MergeConflict(Cell, "base", "mine", "theirs")])
            .Add(c => c.OnResolve, r => captured = r));

        cut.FindAll("input[type=radio]")[1].Change(true); // keep theirs
        cut.Find("button.ms-publish").Click();

        Assert.NotNull(captured);
        Assert.Equal(ConflictChoice.KeepTheirs, Assert.Single(captured!).Choice);
    }
}

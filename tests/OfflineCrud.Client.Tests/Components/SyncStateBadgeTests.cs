using OfflineCrud.Client.Components;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Client.Tests.Components;

[TestFixture]
public class SyncStateBadgeTests : ComponentTestBase
{
    [Test]
    public void Synced_RendersNothingByDefault()
    {
        IRenderedComponent<SyncStateBadge> badge = Context.Render<SyncStateBadge>(parameters => parameters
            .Add(component => component.State, SyncState.Synced));

        Assert.That(badge.Markup.Trim(), Is.Empty);
    }

    [Test]
    public void Synced_WithShowSynced_RendersASyncedBadge()
    {
        IRenderedComponent<SyncStateBadge> badge = Context.Render<SyncStateBadge>(parameters => parameters
            .Add(component => component.State, SyncState.Synced)
            .Add(component => component.ShowSynced, true));

        Assert.That(badge.Find(".badge").TextContent.Trim(), Is.EqualTo("Synced"));
    }

    [TestCase(SyncState.PendingCreate, "New, not synced", "bg-warning-subtle")]
    [TestCase(SyncState.PendingUpdate, "Edited, not synced", "bg-warning-subtle")]
    [TestCase(SyncState.PendingDelete, "Delete not synced", "bg-warning-subtle")]
    [TestCase(SyncState.Failed, "Sync failed", "bg-danger-subtle")]
    public void UnsyncedStates_ShowALabelAndExplanation(SyncState state, string label, string cssClass)
    {
        IRenderedComponent<SyncStateBadge> badge = Context.Render<SyncStateBadge>(parameters => parameters
            .Add(component => component.State, state));

        AngleSharp.Dom.IElement element = badge.Find(".badge");
        Assert.Multiple(() =>
        {
            Assert.That(element.TextContent.Trim(), Is.EqualTo(label));
            Assert.That(element.ClassList, Does.Contain(cssClass));
            Assert.That(element.GetAttribute("title"), Is.Not.Empty, "the title explains the state on hover");
        });
    }
}

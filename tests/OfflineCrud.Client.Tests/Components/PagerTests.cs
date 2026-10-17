using OfflineCrud.Client.Components;

namespace OfflineCrud.Client.Tests.Components;

[TestFixture]
public class PagerTests : ComponentTestBase
{
    [Test]
    public void SinglePage_RendersNothing()
    {
        Assert.That(Render(pageNumber: 1, pageCount: 1).Markup.Trim(), Is.Empty);
    }

    [TestCase(1, 20, new[] { 1, 2, 3, 4, 5 })]
    [TestCase(10, 20, new[] { 8, 9, 10, 11, 12 })]
    [TestCase(20, 20, new[] { 16, 17, 18, 19, 20 })]
    [TestCase(2, 3, new[] { 1, 2, 3 })]
    public void ShowsAWindowOfFivePagesAroundTheCurrentOne(int pageNumber, int pageCount, int[] expected)
    {
        IRenderedComponent<Pager> pager = Render(pageNumber, pageCount);

        int[] shown = [.. pager.FindAll(".page-item:not(:first-child):not(:last-child) button").Select(button => int.Parse(button.TextContent))];
        Assert.That(shown, Is.EqualTo(expected));
    }

    [Test]
    public void CurrentPage_IsMarkedForAssistiveTechnology()
    {
        IRenderedComponent<Pager> pager = Render(pageNumber: 3, pageCount: 5);

        Assert.That(pager.Find("[aria-current=page]").TextContent, Is.EqualTo("3"));
    }

    [Test]
    public void FirstPage_DisablesPrevious_LastPageDisablesNext()
    {
        IRenderedComponent<Pager> first = Render(pageNumber: 1, pageCount: 5);
        IRenderedComponent<Pager> last = Render(pageNumber: 5, pageCount: 5);

        Assert.Multiple(() =>
        {
            Assert.That(first.Find("[aria-label='Previous page']").HasAttribute("disabled"), Is.True);
            Assert.That(first.Find("[aria-label='Next page']").HasAttribute("disabled"), Is.False);
            Assert.That(last.Find("[aria-label='Next page']").HasAttribute("disabled"), Is.True);
        });
    }

    [Test]
    public void Clicking_ReportsThePage()
    {
        List<int> selected = [];
        IRenderedComponent<Pager> pager = Render(pageNumber: 3, pageCount: 5, selected.Add);

        pager.Find("[aria-label='Next page']").Click();
        pager.FindAll(".page-item button").Single(button => button.TextContent == "1").Click();
        pager.Find("[aria-current=page]").Click();

        Assert.That(selected, Is.EqualTo(new[] { 4, 1 }), "clicking the current page does nothing");
    }

    private IRenderedComponent<Pager> Render(int pageNumber, int pageCount, Action<int>? onSelected = null)
    {
        return Context.Render<Pager>(parameters => parameters
            .Add(component => component.PageNumber, pageNumber)
            .Add(component => component.PageCount, pageCount)
            .Add(component => component.OnPageSelected, onSelected ?? (_ => { })));
    }
}

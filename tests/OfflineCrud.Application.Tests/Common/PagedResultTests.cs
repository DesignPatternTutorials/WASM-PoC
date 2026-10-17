using OfflineCrud.Application.Common;

namespace OfflineCrud.Application.Tests.Common;

[TestFixture]
public class PagedResultTests
{
    [TestCase(0, 10, 1)]
    [TestCase(10, 10, 2)]
    [TestCase(190, 10, 20)]
    public void PageNumber_IsOneBased(int skip, int take, int expected)
    {
        Assert.That(Page(itemCount: 0, total: 194, skip, take).PageNumber, Is.EqualTo(expected));
    }

    [TestCase(194, 10, 20)]
    [TestCase(190, 10, 19)]
    [TestCase(0, 10, 1)]
    public void PageCount_RoundsUpAndIsAtLeastOne(int total, int take, int expected)
    {
        Assert.That(Page(itemCount: 0, total, skip: 0, take).PageCount, Is.EqualTo(expected));
    }

    [Test]
    public void HasNextPage_And_HasPreviousPage_ReflectPosition()
    {
        PagedResult<int> first = Page(itemCount: 10, total: 25, skip: 0, take: 10);
        PagedResult<int> last = Page(itemCount: 5, total: 25, skip: 20, take: 10);

        Assert.Multiple(() =>
        {
            Assert.That(first.HasPreviousPage, Is.False);
            Assert.That(first.HasNextPage, Is.True);
            Assert.That(last.HasPreviousPage, Is.True);
            Assert.That(last.HasNextPage, Is.False);
        });
    }

    private static PagedResult<int> Page(int itemCount, int total, int skip, int take)
    {
        return new PagedResult<int>([.. Enumerable.Range(0, itemCount)], total, skip, take);
    }
}

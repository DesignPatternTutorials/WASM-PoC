using OfflineCrud.Application.Sync;

namespace OfflineCrud.Application.Tests.Sync;

[TestFixture]
public class RetryPolicyTests
{
    [TestCase(1, 2)]
    [TestCase(2, 4)]
    [TestCase(3, 8)]
    [TestCase(7, 128)]
    public void DelayFor_DoublesEachAttempt(int attempts, double fullSeconds)
    {
        Assert.Multiple(() =>
        {
            // Equal jitter: between half and all of the exponential delay.
            Assert.That(RetryPolicy.DelayFor(attempts, 0d).TotalSeconds, Is.EqualTo(fullSeconds / 2).Within(0.001));
            Assert.That(RetryPolicy.DelayFor(attempts, 0.999999).TotalSeconds, Is.EqualTo(fullSeconds).Within(0.001));
        });
    }

    [TestCase(9)]
    [TestCase(1000)]
    [TestCase(int.MaxValue)]
    public void DelayFor_IsCapped(int attempts)
    {
        Assert.That(RetryPolicy.DelayFor(attempts, 0.999999), Is.LessThanOrEqualTo(RetryPolicy.MaxDelay));
    }

    [Test]
    public void DelayFor_JitterSpreadsTheDelay()
    {
        Assert.That(RetryPolicy.DelayFor(3, 0.25), Is.Not.EqualTo(RetryPolicy.DelayFor(3, 0.75)));
    }

    [TestCase(0, 0.5)]
    [TestCase(1, -0.1)]
    [TestCase(1, 1.0)]
    public void DelayFor_InvalidArguments_Throw(int attempts, double jitter)
    {
        Assert.That(() => RetryPolicy.DelayFor(attempts, jitter), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}

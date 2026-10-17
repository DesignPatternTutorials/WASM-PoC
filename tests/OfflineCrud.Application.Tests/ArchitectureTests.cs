using System.Reflection;

namespace OfflineCrud.Application.Tests;

/// <summary>
/// Guards the Clean Architecture dependency rule: references point inward only.
/// </summary>
/// <remarks>
/// These check compiled assembly references, so they catch a forbidden dependency as soon as code
/// in the inner layer actually uses a type from an outer one.
/// </remarks>
[TestFixture]
public class ArchitectureTests
{
    private const string SolutionPrefix = "OfflineCrud.";

    [Test]
    public void Domain_DoesNotReferenceAnyOtherSolutionProject()
    {
        IEnumerable<string> references = SolutionReferencesOf("OfflineCrud.Domain");

        Assert.That(references, Is.Empty);
    }

    [Test]
    public void Application_ReferencesOnlyDomain()
    {
        IEnumerable<string> references = SolutionReferencesOf("OfflineCrud.Application");

        Assert.That(references, Is.SubsetOf(new[] { "OfflineCrud.Domain" }));
    }

    private static IEnumerable<string> SolutionReferencesOf(string assemblyName)
    {
        return Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(SolutionPrefix, StringComparison.Ordinal));
    }
}

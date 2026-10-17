namespace OfflineCrud.Client.Tests;

/// <summary>
/// Gives each test a fresh bUnit context: a renderer plus the DI container and fake JS runtime components use.
/// </summary>
/// <remarks>
/// LEARN: bUnit renders components in memory, without a browser. JS interop is faked: in Loose mode every
/// <c>IJSRuntime</c> call returns a default value, so components that import JS modules still render.
/// </remarks>
public abstract class ComponentTestBase
{
    protected BunitContext Context { get; private set; } = null!;

    [SetUp]
    public void CreateContext()
    {
        Context = new BunitContext();
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public async Task DisposeContext()
    {
        await Context.DisposeAsync();
    }
}

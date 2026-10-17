using Microsoft.AspNetCore.Components;
using OfflineCrud.Client.Components;

namespace OfflineCrud.Client.Tests.Components;

[TestFixture]
public class ConfirmDialogTests : ComponentTestBase
{
    [Test]
    public void Closed_RendersNothing()
    {
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: false);

        Assert.That(dialog.Markup.Trim(), Is.Empty);
    }

    [Test]
    public void Open_IsAnAccessibleModalDialog()
    {
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: true);

        AngleSharp.Dom.IElement modal = dialog.Find("[role=dialog]");
        string titleId = modal.GetAttribute("aria-labelledby")!;
        string bodyId = modal.GetAttribute("aria-describedby")!;
        Assert.Multiple(() =>
        {
            Assert.That(modal.GetAttribute("aria-modal"), Is.EqualTo("true"));
            Assert.That(dialog.Find($"#{titleId}").TextContent, Is.EqualTo("Delete product?"));
            Assert.That(dialog.Find($"#{bodyId}").TextContent, Does.Contain("Really?"));
        });
    }

    [Test]
    public void Opening_MovesFocusToCancel()
    {
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: true);

        Context.JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(dialog.Find(".btn-outline-secondary"));
    }

    [Test]
    public void Escape_Cancels()
    {
        int cancelled = 0;
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: true, onCancel: () => cancelled++);

        dialog.Find("[role=dialog]").KeyDown(key: "Escape");

        Assert.That(cancelled, Is.EqualTo(1));
    }

    [Test]
    public void Confirm_InvokesOnConfirm()
    {
        int confirmed = 0;
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: true, onConfirm: () => confirmed++);

        dialog.Find(".btn-danger").Click();

        Assert.That(confirmed, Is.EqualTo(1));
    }

    [Test]
    public void Busy_DisablesEveryButton_AndIgnoresEscape()
    {
        int cancelled = 0;
        IRenderedComponent<ConfirmDialog> dialog = Render(isOpen: true, isBusy: true, onCancel: () => cancelled++);

        dialog.Find("[role=dialog]").KeyDown(key: "Escape");

        Assert.Multiple(() =>
        {
            Assert.That(dialog.FindAll("button").All(button => button.HasAttribute("disabled")), Is.True);
            Assert.That(cancelled, Is.Zero);
        });
    }

    private IRenderedComponent<ConfirmDialog> Render(bool isOpen, bool isBusy = false, Action? onConfirm = null, Action? onCancel = null)
    {
        return Context.Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.IsOpen, isOpen)
            .Add(component => component.IsBusy, isBusy)
            .Add(component => component.Title, "Delete product?")
            .Add(component => component.ConfirmText, "Delete")
            .Add(component => component.ConfirmButtonClass, "btn-danger")
            .Add(component => component.OnConfirm, onConfirm ?? (() => { }))
            .Add(component => component.OnCancel, onCancel ?? (() => { }))
            .AddChildContent((RenderFragment)(builder => builder.AddContent(0, "Really?"))));
    }
}

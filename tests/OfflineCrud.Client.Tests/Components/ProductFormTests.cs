using OfflineCrud.Application.Products;
using OfflineCrud.Client.Components;

namespace OfflineCrud.Client.Tests.Components;

[TestFixture]
public class ProductFormTests : ComponentTestBase
{
    private static readonly string[] Categories = ["beauty", "home-decoration"];

    [Test]
    public void EmptyForm_DoesNotSubmit_AndExplainsWhatIsMissing()
    {
        List<ProductFormModel> submitted = [];
        IRenderedComponent<ProductForm> form = Render(new ProductFormModel(), submitted.Add);

        form.Find("form").Submit();

        Assert.Multiple(() =>
        {
            Assert.That(submitted, Is.Empty);
            Assert.That(form.FindAll(".validation-message").Select(message => message.TextContent),
                Has.Some.Contains("Title").And.Some.EqualTo("Choose a category."));
            Assert.That(form.Find("#title").ClassList, Does.Contain("is-invalid"), "Bootstrap styling from BootstrapFieldCssClassProvider");
            Assert.That(form.Find("#title").GetAttribute("aria-invalid"), Is.EqualTo("true"), "announced as invalid");
        });
    }

    [Test]
    public void ValidForm_SubmitsTheModel()
    {
        List<ProductFormModel> submitted = [];
        IRenderedComponent<ProductForm> form = Render(new ProductFormModel(), submitted.Add);

        form.Find("#title").Change("Lamp");
        form.Find("#category").Change("home-decoration");
        form.Find("#description").Change("A desk lamp.");
        form.Find("#price").Change("19.99");
        form.Find("#stock").Change("4");
        form.Find("form").Submit();

        ProductFormModel model = submitted.Single();
        Assert.Multiple(() =>
        {
            Assert.That(model.Title, Is.EqualTo("Lamp"));
            Assert.That(model.Category, Is.EqualTo("home-decoration"));
            Assert.That(model.Price, Is.EqualTo(19.99m));
            Assert.That(model.ToDetails().Stock, Is.EqualTo(4));
        });
    }

    [Test]
    public void OutOfRangeDiscount_IsRejected()
    {
        List<ProductFormModel> submitted = [];
        ProductFormModel model = new() { Title = "Lamp", Category = "beauty", Description = "d", Price = 1m, Stock = 1 };
        IRenderedComponent<ProductForm> form = Render(model, submitted.Add);

        form.Find("#discount").Change("150");
        form.Find("form").Submit();

        Assert.Multiple(() =>
        {
            Assert.That(submitted, Is.Empty);
            Assert.That(form.FindAll(".validation-message").Select(message => message.TextContent), Has.Some.Contains("Discount %"));
        });
    }

    [Test]
    public void Saving_DisablesTheFormAndShowsProgress()
    {
        IRenderedComponent<ProductForm> form = Render(new ProductFormModel(), _ => { }, isSaving: true);

        Assert.Multiple(() =>
        {
            Assert.That(form.Find("fieldset").HasAttribute("disabled"), Is.True);
            Assert.That(form.Find("button[type=submit]").TextContent, Does.Contain("Saving…"));
        });
    }

    [Test]
    public void CurrentCategory_StaysSelectable_EvenIfNotInTheList()
    {
        ProductFormModel model = new() { Category = "vintage" };

        IRenderedComponent<ProductForm> form = Render(model, _ => { });

        Assert.That(form.FindAll("#category option").Select(option => option.GetAttribute("value")),
            Is.EqualTo(new[] { "", "beauty", "home-decoration", "vintage" }));
    }

    private IRenderedComponent<ProductForm> Render(ProductFormModel model, Action<ProductFormModel> onValidSubmit, bool isSaving = false)
    {
        return Context.Render<ProductForm>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.Categories, Categories)
            .Add(component => component.IsSaving, isSaving)
            .Add(component => component.OnValidSubmit, onValidSubmit));
    }
}

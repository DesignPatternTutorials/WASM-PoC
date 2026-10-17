using Microsoft.AspNetCore.Components.Forms;

namespace OfflineCrud.Client.Components;

/// <summary>
/// Makes Blazor's form inputs use Bootstrap's <c>is-valid</c> / <c>is-invalid</c> classes
/// instead of Blazor's default <c>valid</c> / <c>invalid</c>.
/// </summary>
public sealed class BootstrapFieldCssClassProvider : FieldCssClassProvider
{
    /// <inheritdoc />
    public override string GetFieldCssClass(EditContext editContext, in FieldIdentifier fieldIdentifier)
    {
        bool isValid = !editContext.GetValidationMessages(fieldIdentifier).Any();

        // Only flag a field once the user has touched it, so a fresh form isn't covered in green ticks.
        if (!editContext.IsModified(fieldIdentifier))
        {
            return isValid ? string.Empty : "is-invalid";
        }

        return isValid ? "is-valid" : "is-invalid";
    }
}

namespace SalesInventory.Web.Services;

public enum ToastKind
{
    Success,
    Error
}

/// <summary>One notification waiting to be shown. The id lets the container close exactly this toast.</summary>
public record ToastMessage(Guid Id, ToastKind Kind, string Text);

/// <summary>
/// Lets any page or service raise a toast, even right before navigating away.
/// Scoped, so each browser circuit has its own; the ToastContainer in the layout listens to <see cref="Shown"/>.
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? Shown;

    public void ShowSuccess(string message) => Show(ToastKind.Success, message);

    public void ShowError(string message) => Show(ToastKind.Error, message);

    private void Show(ToastKind kind, string message) => Shown?.Invoke(new ToastMessage(Guid.NewGuid(), kind, message));
}

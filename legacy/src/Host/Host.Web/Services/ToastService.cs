namespace Host.Web.Services;

public sealed class ToastService
{
    public event Action<ToastMessage>? OnShow;

    public void ShowSuccess(string message) =>
        OnShow?.Invoke(new(ToastSeverity.Success, message));

    public void ShowError(string message) =>
        OnShow?.Invoke(new(ToastSeverity.Error, message));

    public void ShowWarning(string message) =>
        OnShow?.Invoke(new(ToastSeverity.Warning, message));

    public void ShowInfo(string message) =>
        OnShow?.Invoke(new(ToastSeverity.Info, message));
}

public record ToastMessage(ToastSeverity Severity, string Message);

public enum ToastSeverity
{
    Success,
    Error,
    Warning,
    Info
}

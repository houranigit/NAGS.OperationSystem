namespace Host.Web.Services;

public sealed class PageTitleService
{
    public event Action? OnChanged;

    public string Title { get; private set; } = "";
    public string? Description { get; private set; }

    public void Set(string title, string? description = null)
    {
        Title = title;
        Description = description;
        OnChanged?.Invoke();
    }
}

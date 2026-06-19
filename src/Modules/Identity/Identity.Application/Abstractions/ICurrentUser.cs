namespace Identity.Application.Abstractions;

/// <summary>The authenticated principal for the current request, if any.</summary>
public interface ICurrentUser
{
    public Guid? UserId { get; }

    public bool IsAuthenticated { get; }
}

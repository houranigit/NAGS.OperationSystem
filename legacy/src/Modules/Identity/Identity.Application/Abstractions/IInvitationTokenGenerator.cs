namespace Identity.Application.Abstractions;

public interface IInvitationTokenGenerator
{
    string Generate();
    TimeSpan ExpiryDuration { get; }
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BuildingBlocks.Application.Abstractions;

namespace MasterData.IntegrationTests;

/// <summary>
/// Captures emails at the real delivery boundary. Invitations flow through the durable email outbox
/// (encrypted body) and are sent here, so tests obtain the raw activation token by parsing the
/// delivered message after draining the outbox.
/// </summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _bodiesByEmail = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _bodiesByEmail[message.ToEmail] = message.HtmlBody;
        return Task.CompletedTask;
    }

    public string? TokenFor(string email)
    {
        if (!_bodiesByEmail.TryGetValue(email, out var body))
            return null;

        var match = TokenRegex().Match(body);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("token=([A-Za-z0-9._~%\\-]+)")]
    private static partial Regex TokenRegex();
}

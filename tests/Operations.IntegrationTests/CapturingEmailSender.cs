using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BuildingBlocks.Application.Abstractions;

namespace Operations.IntegrationTests;

/// <summary>
/// Captures emails at the real delivery boundary so tests can obtain raw activation tokens after
/// draining the durable email outbox.
/// </summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _bodiesByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public bool IsEnabled => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _bodiesByEmail[message.ToEmail] = message.HtmlBody;
        _messages.Enqueue(message);
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

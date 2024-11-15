using Postmark.Model.Suppressions;
using PostmarkDotNet;

namespace NewsletterBuilder;

public class Mailer
{
  private static PostmarkClient _client;
  private static bool _isDevelopment;

  public static void Configure(string postmarkServerToken, bool isDevelopment)
  {
    _client = new PostmarkClient(postmarkServerToken);
    _isDevelopment = isDevelopment;
  }

  private readonly List<PostmarkMessage> _messages = [];
  private int _totalMessages;

  public void Enqueue(string to, string subject, string from, string replyTo, bool isBroadcast, string html, string plainText = null)
  {
    if (_isDevelopment && ++_totalMessages > 2) return;
    if (_messages.Count >= 500) throw new InvalidOperationException("Too many messages queued");
    _messages.Add(new PostmarkMessage
    {
      To = _isDevelopment ? replyTo : to,
      From = from,
      ReplyTo = replyTo,
      Subject = subject,
      HtmlBody = html,
      TextBody = plainText,
      MessageStream = isBroadcast ? "broadcast" : "outbound",
      Tag = $"{(isBroadcast ? "Newsletter" : "Reminder")}{(_isDevelopment ? " Test" : string.Empty)}",
      TrackOpens = false,
      TrackLinks = LinkTrackingOptions.None
    });
  }

  public async Task SendAsync()
  {
    if (_messages.Count == 0) return;
    await _client.SendMessagesAsync(_messages);
    _messages.Clear();
  }

  public static async Task<HashSet<string>> GetSuppressedRecipientsAsync()
  {
    var suppressionResponse = await _client.ListSuppressions(new PostmarkSuppressionQuery(), "broadcast");
    return suppressionResponse.Suppressions.Select(o => o.EmailAddress.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
  }
}

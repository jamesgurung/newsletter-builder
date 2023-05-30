using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class RecipientData
{
  [JsonPropertyName("recipients")]
  public IList<string> Recipients { get; init; }
}
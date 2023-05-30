using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class PublishData
{
  [JsonPropertyName("html")]
  public string Html { get; init; }

  [JsonPropertyName("description")]
  public string Description { get; init; }
}
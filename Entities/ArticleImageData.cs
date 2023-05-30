using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class ArticleImageData
{
  [JsonPropertyName("filename")]
  public string FileName { get; init; }
  [JsonPropertyName("url")]
  public string Url { get; init; }
}
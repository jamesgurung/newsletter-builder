using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class NewsletterListItem
{
  [JsonPropertyName("date")]
  public string Date { get; set; }

  [JsonPropertyName("description")]
  public string Description { get; set; }
}

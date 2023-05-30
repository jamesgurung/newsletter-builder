using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class ArticleContentData
{
  [JsonPropertyName("headline")]
  public string Headline { get; init; }
  [JsonPropertyName("sections")]
  public IList<ArticleSection> Sections { get; init; }
}

public class ArticleSection
{
  [JsonPropertyName("text")]
  public string Text { get; init; }
  [JsonPropertyName("includeImage")]
  public bool IncludeImage { get; init; }
  [JsonPropertyName("image")]
  public string Image { get; init; }
  [JsonPropertyName("alt")]
  public string Alt { get; init; }
  [JsonPropertyName("consent")]
  public bool Consent { get; init; }
  [JsonPropertyName("consentNotes")]
  public string ConsentNotes { get; init; }
  [JsonIgnore, IgnoreDataMember]
  public string ImageRenderName { get; set; }
}
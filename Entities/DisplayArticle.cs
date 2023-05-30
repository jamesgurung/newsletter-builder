namespace NewsletterBuilder.Entities;

public class DisplayArticle
{
  public string ShortName { get; init; }
  public ArticleContentData Content { get; init; }
  public string AuthorDisplayName { get; init; }
}

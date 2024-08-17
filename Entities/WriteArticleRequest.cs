namespace NewsletterBuilder.Entities;

public class WriteArticleRequest
{
  public string Identifier { get; set; }
  public string Headline { get; set; }
  public string Content { get; set; }
  public int Paragraphs { get; set; }
}

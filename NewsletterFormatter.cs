using AngleSharp.Dom;
using AngleSharp;
using System.Text;

namespace NewsletterBuilder;

public static class NewsletterFormatter
{
  public static async Task<NewsletterFormats> FormatAsync(string originalHtml, string twitterHandle)
  {
    var htmlTransform = PreMailer.Net.PreMailer.MoveCssInline(originalHtml, removeStyleElements: true, ignoreElements: "#media,#webstyles");
    var htmlBody = htmlTransform.Html;
    var browser = BrowsingContext.New(Configuration.Default);
    var document = await browser.OpenAsync(req => req.Content(htmlBody));
    var title = document.GetElementsByTagName("h1").FirstOrDefault().TextContent.Trim();
    var intro = document.GetElementById("intro").TextContent.Trim();
    var preheader = document.GetElementById("preheader");
    preheader.TextContent = intro;

    var webDocument = await browser.OpenAsync(req => req.Content(document.ToHtml()));
    webDocument.GetElementById("footer").Remove();
    webDocument.GetElementsByTagName("hr").Last().Remove();
    webDocument.Head.InnerHtml +=
      $"\n  <title>{title}</title>\n" +
      $"  <meta name=\"description\" content=\"{intro.Replace("\"", "&quot;", StringComparison.OrdinalIgnoreCase)}\" />\n" +
      $"  <meta name=\"twitter:card\" content=\"summary_large_image\" />\n" +
      $"  <meta name=\"twitter:site\" content=\"{twitterHandle}\" />\n" +
      $"  <meta property=\"og:title\" content=\"{title.Replace("\"", "&quot;", StringComparison.OrdinalIgnoreCase)}\" />\n" +
      $"  <meta property=\"og:type\" content=\"website\" />\n";
    foreach (var comment in webDocument.Descendants<IComment>())
    {
      comment.Remove();
    }
    var webHtml = string.Join('\n', webDocument.ToHtml().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));

    document.GetElementById("webstyles").Remove();
    document.GetElementById("webheader").Remove();
    document.GetElementById("webscript").Remove();
    document.QuerySelector("meta[property=\"og:image\"]").Remove();
    htmlBody = string.Join('\n', document.ToHtml().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));

    var sb = new StringBuilder();
    foreach (var element in document.QuerySelectorAll("h1,h2,p,li"))
    {
      var type = element.TagName.ToLowerInvariant();
      switch (type)
      {
        case "h1":
        case "h2":
          {
            var headingText = element.TextContent.Trim();
            var charLine = new string(type == "h1" ? '*' : '-', Math.Min(headingText.Length, 80));
            sb.AppendLine();
            sb.AppendLine(charLine);
            sb.AppendLine(headingText);
            sb.AppendLine(charLine);
            sb.AppendLine();
            break;
          }
        case "p":
          sb.AppendLine();
          sb.AppendLine(FormatToPlainText(element));
          sb.AppendLine();
          break;
        case "li":
          sb.AppendLine("* " + FormatToPlainText(element));
          break;
        default:
          break;
      }
    }
    sb.Replace(Environment.NewLine + Environment.NewLine + Environment.NewLine, Environment.NewLine + Environment.NewLine);
    var textBody = sb.ToString().Trim();

    return new()
    {
      WebHtml = webHtml,
      EmailHtml = htmlBody,
      EmailPlainText = textBody
    };
  }

  private static string FormatToPlainText(IElement element)
  {
    if (element.ChildNodes.Length == 0) return element.TextContent.Trim();
    var result = string.Empty;
    foreach (var child in element.ChildNodes)
    {
      if (child is IElement childElement)
      {
        var type = childElement.TagName.ToLowerInvariant();
        if (type == "a")
        {
          var href = childElement.Attributes["href"].Value.Replace("mailto:", string.Empty, StringComparison.OrdinalIgnoreCase);
          var text = childElement.TextContent.Trim();
          if (href == text) result += text;
          else result += $"{text} ({href})";
        }
        else if (type is "b" or "strong")
        {
          result += $"*{FormatToPlainText(childElement)}*";
        }
        else if (type == "br")
        {
          result += Environment.NewLine;
        }
        else
        {
          result += FormatToPlainText(childElement);
        }
      }
      else if (child is IText text)
      {
        result += text.TextContent;
      }
    }
    return result.Trim();
  }
}

public class NewsletterFormats
{
  public string WebHtml { get; init; }
  public string EmailHtml { get; init; }
  public string EmailPlainText { get; init; }
}
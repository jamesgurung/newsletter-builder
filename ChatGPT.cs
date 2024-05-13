using Microsoft.AspNetCore.SignalR;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewsletterBuilder;

public class ChatGPT(HttpClient client, IHubClients<IChatClient> hub, string chatId)
{
  public static string ModelName { get; set; }

  public async Task<string> RequestArticleFeedbackAsync(string headline, string text, string identifier)
  {

    var prompt1 = $"This article is part of a weekly Academy newsletter. It is intended to engage parents, promote a positive culture, " +
      $"and showcase our enriching student experience.\n\n" +
      $"Headline:\n{headline}\n\n" +
      $"Article content:\n{text}\n\n" +
      $"###\n" +
      $"Review the spelling, punctuation, and grammar. If it is all correct, write a single bullet point to praise it. " +
      $"Otherwise, use bullet points to state each of the mistakes and how to fix them. Do not give stylistic feedback; " +
      $"only correct mistakes that are clearly wrong.";

    var prompt2 = "Answer the following questions about the article:\n" +
      $"* How engaging is the headline? Suggest three alternative suggestions as a comma-separated list.\n" +
      $"* Provide positive feedback on the article, giving this praise in a warm tone.\n" +
      $"* Provide constructive feedback on the content of the article, not mentioning spelling, punctuation, or grammar. How could it be better? Do not suggest including quotes.\n" +
      $"* Provide feedback on the writing style (it should be a balance of professional and casual, upbeat, and highly engaging). " +
      $"Give examples of how parts could be reworded, if this is needed.";

    var spagResponse = await SendGptRequestAsync([new() { Role = "user", Content = prompt1 }], 0m, identifier);
    var reviewPrompts = new List<ChatGPTMessage>() {
      new() { Role = "user", Content = prompt1 },
      new() { Role = "assistant", Content = spagResponse },
      new() { Role = "user", Content = prompt2 }
    };
    var reviewResponse = await SendGptRequestAsync(reviewPrompts, 0.2m, identifier);
    return $"{spagResponse}\n{reviewResponse}";
  }

  private async Task<string> SendGptRequestAsync(List<ChatGPTMessage> prompts, decimal temperature, string identifier) {
    var systemPrompt = "You are a friendly and helpful assistant. " +
      "You always respond in bullet points, using a '*' character, with no introduction. " +
      "You do not use subheadings or bullet point headings. " +
      "You write each bullet point in clear prose, without an introduction or subtitle. " +
      "You do not use nested bullet points. " +
      "You do not comment on layout or paragraphing. " +
      "You do not comment on date formats. " +
      "You do not comment on photos or captions. " +
      "You use British English and approve of the Oxford comma.";
    prompts.Insert(0, new() { Role = "system", Content = systemPrompt });

    var request = new ChatGPTRequest
    {
      User = identifier,
      Temperature = temperature,
      Choices = 1,
      Stream = true,
      Messages = prompts,
      Model = ModelName
    };
    for (var attempt = 1; attempt <= 3; attempt++)
    {
      using var body = JsonContent.Create(request);
      using var message = new HttpRequestMessage(HttpMethod.Post, string.Empty) { Content = body };
      using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
      if (!response.IsSuccessStatusCode)
      {
        await Task.Delay(1000 * (int)Math.Pow(2, attempt));
        continue;
      }
      using var stream = await response.Content.ReadAsStreamAsync();
      using var reader = new StreamReader(stream);
      var content = new StringBuilder();
      while (!reader.EndOfStream)
      {
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
        if (line == "data: [DONE]") break;
        var chunk = JsonSerializer.Deserialize<ChatGPTResponse>(line[6..]);
        if (chunk?.Value is null) continue;
        content.Append(chunk.Value);
        await hub.Client(chatId).Type(chunk.Value);
      }
      return content.ToString();
    }
    throw new HttpRequestException("GPT request failed.");
  }
}

public class ChatGPTRequest
{
  [JsonPropertyName("messages")]
  public IList<ChatGPTMessage> Messages { get; set; }
  [JsonPropertyName("user")]
  public string User { get; set; }
  [JsonPropertyName("temperature")]
  public decimal Temperature { get; set; }
  [JsonPropertyName("n")]
  public decimal Choices { get; set; }
  [JsonPropertyName("stream")]
  public bool Stream { get; set; }
  [JsonPropertyName("model")]
  public string Model { get; set; }
}

public class ChatGPTMessage
{
  [JsonPropertyName("role")]
  public string Role { get; set; }
  [JsonPropertyName("content")]
  public string Content { get; set; }
}


public class ChatGPTResponse
{
  [JsonPropertyName("choices")]
  public IList<ChatGPTResponseChoice> Choices { get; set; }

  [JsonIgnore]
  public string Value => Choices?[0].Delta?.Content;
}

public class ChatGPTResponseChoice
{
  [JsonPropertyName("delta")]
  public ChatGPTResponseMessage Delta { get; set; }
}

public class ChatGPTResponseMessage
{
  [JsonPropertyName("content")]
  public string Content { get; set; }
}

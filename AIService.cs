using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace NewsletterBuilder;

public static class AIService
{
  public static void Configure(string endpoint, string deploymentName, string apiKey)
  {
    var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    _client = azureClient.GetChatClient(deploymentName);
  }

  private static ChatClient _client;

  private static readonly string[] photoTypes = ["people", "student work", "other"];

  private static readonly ChatTool describePhotoTool = ChatTool.CreateFunctionTool("describe_photo", null, BinaryData.FromObjectAsJson(new
  {
    Type = "object",
    Properties = new
    {
      AltText = new
      {
        Type = "string",
        Description = "Very short description of what is in the photo, to be included as alt text for screen readers."
      },
      Subject = new
      {
        Type = "string",
        Enum = photoTypes,
        Description = "Categorise the subject of the photo. If it shows one or more students or teachers, say 'people'. If it shows student work, for example artwork or an exercise book, " +
        "say 'student work'. For anything else, say 'other'."
      }
    },
    Required = new[] { "AltText", "Subject" }
  }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

  private static readonly ChatTool writeArticleTool = ChatTool.CreateFunctionTool("write_article", null, BinaryData.FromObjectAsJson(new
  {
    Type = "object",
    Properties = new
    {
      Headline = new
      {
        Type = "string",
        Description = "A short, engaging headline"
      },
      Body = new
      {
        Type = "array",
        Description = "An array of very short paragraphs that make up the main text of the article. Each paragraph is 2-3 sentences.",
        Items = new { Type = "string" }
      }
    },
    Required = new[] { "Headline", "Body" }
  }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

  public static async Task<string> DescribePhotoAsync(Uri photoUri, string title, string identifier)
  {
    var messages = new List<ChatMessage>
    {
      new SystemChatMessage("You are a helpful assistant who describes photographs. Be very brief, answering in a few words only. " +
        "The photographs are for use in an Academy newsletter, and may include students, staff, or student work. When referring to people, " +
        "use educational terms like 'teacher' and 'student'. Use the article topic to put the description in context. Use British English spelling."),
      new UserChatMessage(
        ChatMessageContentPart.CreateTextPart($"This photo is from an article on {title}. What does it show?"),
        ChatMessageContentPart.CreateImagePart(photoUri, ChatImageDetailLevel.Low)
      )
    };

    var options = new ChatCompletionOptions
    {
      Temperature = 0,
      EndUserId = identifier,
      Tools = { describePhotoTool },
      ToolChoice = ChatToolChoice.CreateRequiredChoice()
    };

    var response = await _client.CompleteChatAsync(messages, options);
    if (response?.Value is null) return null;
    var args = response.Value.ToolCalls[0].FunctionArguments;
    var data = JsonSerializer.Deserialize<AIPhotoResponse>(args);
    return (data.Subject == "other") ? "invalid" : data.AltText.TrimEnd('.');
  }

  public static async IAsyncEnumerable<string> RequestArticleFeedbackAsync(string headline, string text, string identifier)
  {
    var systemPrompt = "You are a friendly and helpful assistant. " +
      "You always respond in bullet points, using a '*' character, with no introduction. " +
      "You do not use subheadings or bullet point headings. " +
      "You write each bullet point in clear prose, without an introduction or subtitle. " +
      "You do not use nested bullet points. " +
      "You do not comment on layout or paragraphing. " +
      "You do not comment on date formats. " +
      "You do not comment on photos or captions. " +
      "You use British English and approve of the Oxford comma.";

    var spagPrompt = "This article is part of a weekly Academy newsletter. It is intended to engage parents, promote a positive culture, " +
      "and showcase our enriching student experience.\n\n" +
      $"Headline:\n{headline}\n\n" +
      $"Article content:\n{text}\n\n" +
      "###\n" +
      "Review the spelling, punctuation, and grammar. If it is all correct, write a single bullet point to praise it. " +
      "Otherwise, use bullet points to state each of the mistakes and how to fix them. Do not give stylistic feedback; " +
      "only correct mistakes that are clearly wrong.";

    var stylePrompt = "Answer the following questions about the article:\n" +
      "* How engaging is the headline? Suggest three alternative suggestions as a comma-separated list.\n" +
      "* Provide positive feedback on the article, giving this praise in a warm tone.\n" +
      "* Provide constructive feedback on the content of the article, not mentioning spelling, punctuation, or grammar. How could it be better? Do not suggest including quotes.\n" +
      "* Provide feedback on the writing style (it should be a balance of professional and casual, upbeat, and highly engaging). " +
      "Give a few examples of how parts could be reworded, if this is needed.";

    var spagStreamingResult = _client.CompleteChatStreamingAsync([new SystemChatMessage(systemPrompt), new UserChatMessage(spagPrompt)],
      new() { Temperature = 0.1f, TopP = 0.5f, EndUserId = identifier });
    var spagBuilder = new StringBuilder();

    await foreach (var update in spagStreamingResult)
    {
      foreach (var part in update.ContentUpdate)
      {
        if (string.IsNullOrEmpty(part.Text)) continue;
        spagBuilder.Append(part.Text);
        yield return part.Text;
      }
    }
    yield return "\n";

    var styleStreamingResult = _client.CompleteChatStreamingAsync([new SystemChatMessage(systemPrompt), new UserChatMessage(spagPrompt), new AssistantChatMessage(spagBuilder.ToString()),
      new UserChatMessage(stylePrompt)], new() { Temperature = 0.3f, TopP = 0.8f, EndUserId = identifier });

    await foreach (var update in styleStreamingResult)
    {
      foreach (var part in update.ContentUpdate)
      {
        if (string.IsNullOrEmpty(part.Text)) continue;
        yield return part.Text;
      }
    }
  }

  public static async Task<AIArticleResponse> WriteArticleAsync(string headline, string content, int paragraphs, string identifier)
  {
    var messages = new List<ChatMessage>
    {
      new SystemChatMessage("You are a helpful assistant who writes articles for an Academy newsletter. " +
        "You write in a warm, engaging tone, using British English spelling and grammar. " +
        "You use the Oxford comma. You write short paragraphs of 2-3 sentences. " +
        "You do not use subheadings or bullet points."),
      new UserChatMessage("Write an article for our newsletter.\n\n" +
        $"Topic: {headline}\n\n" +
        $"Key points to include: {content}\n\n" +
        $"Please write a headline and exactly {paragraphs} paragraphs.")
    };

    var options = new ChatCompletionOptions
    {
      Temperature = 0.5f,
      TopP = 0.8f,
      FrequencyPenalty = 0.1f,
      EndUserId = identifier,
      Tools = { writeArticleTool },
      ToolChoice = ChatToolChoice.CreateRequiredChoice()
    };

    var response = await _client.CompleteChatAsync(messages, options);
    var args = response.Value.ToolCalls[0].FunctionArguments;
    return JsonSerializer.Deserialize<AIArticleResponse>(args);
  }
}

public class AIPhotoResponse
{
  [JsonPropertyName("altText")]
  public string AltText { get; set; }
  [JsonPropertyName("subject")]
  public string Subject { get; set; }
}

public class AIArticleResponse
{
  [JsonPropertyName("headline")]
  public string Headline { get; set; }
  [JsonPropertyName("body")]
  public IList<string> Body { get; set; }
}
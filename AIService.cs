using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace NewsletterBuilder;

#pragma warning disable OPENAI001

public static class AIService
{
  private static OpenAIResponseClient _client;

  public static void Configure(string endpoint, string deploymentName, string apiKey)
  {
    var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    _client = azureClient.GetOpenAIResponseClient(deploymentName);
  }

  private static readonly BinaryData describePhotoSchema = BinaryData.FromBytes("""
    {
      "type": "object",
      "properties": {
        "altText": {
          "type": "string",
          "description": "Very short description of what is in the photo, to be included as alt text for screen readers."
        },
        "subject": {
          "type": "string",
          "enum": [ "people", "student work", "other" ],
          "description": "Categorise the subject of the photo. If it shows one or more students or teachers, say 'people'. If it shows student work, for example artwork or an exercise book, say 'student work'. For anything else, say 'other'."
        }
      },
      "required": ["altText", "subject"], "additionalProperties": false
    }
    """u8.ToArray());

  private static readonly BinaryData writeArticleSchema = BinaryData.FromBytes("""
    {
      "type": "object",
      "properties": {
        "headline": {
          "type": "string",
          "description": "A short, engaging headline"
        },
        "body": {
          "type": "array",
          "description": "An array of very short paragraphs that make up the main text of the article. Each paragraph is 2-3 sentences.",
          "items": { "type": "string" }
        }
      },
      "required": ["headline", "body"], "additionalProperties": false
    }
    """u8.ToArray());

  public static async Task<string> DescribePhotoAsync(Uri photoUri, string title, string identifier)
  {
    var instructions = """
      You are a helpful assistant who describes photographs.
      Be very brief, answering in a few words only.
      The photographs are for use in a school newsletter, and may include students, staff, or student work.
      When referring to people, use educational terms like 'teacher' and 'student'.
      Use the article topic to put the description in context.
      Use British English spelling.
      """;

    var userMessage = ResponseItem.CreateUserMessageItem([
      ResponseContentPart.CreateInputTextPart($"This photo is from an article on {title}. What does it show?"),
      ResponseContentPart.CreateInputImagePart(photoUri, ResponseImageDetailLevel.Low)
    ]);

    var options = new ResponseCreationOptions
    {
      Instructions = instructions,
      ReasoningOptions = new ResponseReasoningOptions { ReasoningEffortLevel = "low" },
      StoredOutputEnabled = false,
      TextOptions = new ResponseTextOptions { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("photo", describePhotoSchema, jsonSchemaIsStrict: true) },
      EndUserId = identifier
    };

    var response = await _client.CreateResponseAsync([userMessage], options);
    var json = response.Value.OutputItems.Select(o => o as MessageResponseItem).First(o => o is not null).Content.First().Text;
    var photoDescription = JsonSerializer.Deserialize<AIPhotoResponse>(json);
    return (photoDescription.Subject == "other") ? "invalid" : photoDescription.AltText.TrimEnd('.');
  }

  public static async IAsyncEnumerable<string> RequestArticleFeedbackAsync(string headline, string text, string identifier)
  {
    var instructions = """
      You are a friendly and helpful assistant.
      You always respond in bullet points, using a '*' character, with no introduction.
      You do not use subheadings or bullet point headings.
      You write each bullet point in clear prose, without an introduction or subtitle.
      You do not use nested bullet points.
      You do not comment on layout or paragraphing.
      You do not comment on date formats.
      You do not comment on photos or captions.
      You use British English spelling and terminology, and approve of the Oxford comma.
      """;

    var spagPrompt = $"""
      This article is part of a weekly school newsletter. It is intended to engage parents, promote a positive culture, and showcase our enriching student experience.

      # Headline:
      {headline}

      # Article content:
      {text}

      # Task:
      Review the spelling, punctuation, and grammar. If it is all correct, write a single bullet point to praise it.
      Otherwise, use bullet points to state each of the mistakes and how to fix them. Do not give stylistic feedback; only correct mistakes that are clearly wrong." +
      """;

    var stylePrompt = """
      Answer the following questions about the article:
      * How engaging is the headline? Suggest three alternative suggestions as a comma-separated list.
      * Provide positive feedback on the article, giving this praise in a warm tone.
      * Provide constructive feedback on the content of the article, not mentioning spelling, punctuation, or grammar. How could it be better? Do not suggest including quotes.
      * Provide feedback on the writing style (it should be a balance of professional and casual, upbeat, and highly engaging). Give a few examples of how parts could be reworded, if this is needed.
      """;

    var options = new ResponseCreationOptions
    {
      Instructions = instructions,
      ReasoningOptions = new ResponseReasoningOptions { ReasoningEffortLevel = "low" },
      StoredOutputEnabled = false,
      EndUserId = identifier
    };

    var spagUserMessage = ResponseItem.CreateUserMessageItem(spagPrompt);
    var spagStreamingResult = _client.CreateResponseStreamingAsync([spagUserMessage], options);
    var spagBuilder = new StringBuilder();

    await foreach (var update in spagStreamingResult)
    {
      if (update is not StreamingResponseOutputTextDeltaUpdate chunk || string.IsNullOrEmpty(chunk?.Delta)) continue;
      spagBuilder.Append(chunk.Delta);
      yield return chunk.Delta;
    }
    yield return "\n";

    var spagAssistantMessage = ResponseItem.CreateAssistantMessageItem(spagBuilder.ToString());
    var styleUserMessage = ResponseItem.CreateUserMessageItem(stylePrompt);
    var styleStreamingResult = _client.CreateResponseStreamingAsync([spagUserMessage, spagAssistantMessage, styleUserMessage], options);

    await foreach (var update in styleStreamingResult)
    {
      if (update is not StreamingResponseOutputTextDeltaUpdate chunk || string.IsNullOrEmpty(chunk?.Delta)) continue;
      yield return chunk.Delta;
    }
  }

  public static async Task<AIArticleResponse> WriteArticleAsync(string headline, string content, int paragraphs, string identifier)
  {
    var instructions = """
      You are a helpful assistant who writes articles for an Academy newsletter.
      You write in a warm, engaging tone, using British English spelling and grammar.
      You use the Oxford comma. You write short paragraphs of 2-3 sentences.
      You do not use subheadings or bullet points.
      """;

    var userMessage = ResponseItem.CreateUserMessageItem($"""
      Write an article for our newsletter.
      Topic: {headline}
      Key points to include: {content}
      Please write a headline and exactly {paragraphs} paragraphs.
      """);

    var options = new ResponseCreationOptions
    {
      Instructions = instructions,
      ReasoningOptions = new ResponseReasoningOptions { ReasoningEffortLevel = "medium" },
      StoredOutputEnabled = false,
      TextOptions = new ResponseTextOptions { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("article", writeArticleSchema, jsonSchemaIsStrict: true) },
      EndUserId = identifier
    };

    var response = await _client.CreateResponseAsync([userMessage], options);
    var json = response.Value.OutputItems.Select(o => o as MessageResponseItem).First(o => o is not null).Content.First().Text;
    return JsonSerializer.Deserialize<AIArticleResponse>(json);
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
using Azure;
using Azure.Data.Tables;
using NewsletterBuilder.Entities;
using System.Globalization;

namespace NewsletterBuilder;

public class TableService(string domain)
{
  public static void Configure(string connectionString)
  {
    client = new TableServiceClient(connectionString);
  }

  private static TableServiceClient client;
  private static readonly string[] selectPartitionKey = ["PartitionKey"];
  private static readonly string[] selectRowKey = ["RowKey"];

  // Reduces cold start latency by several seconds
  public static async Task WarmUpAsync()
  {
    var nonExistentKey = "warmup";
    var tasks = new[]
    {
      client.GetTableClient("newsletters").QueryAsync<TableEntity>(o => o.PartitionKey == nonExistentKey).ToListAsync(),
      client.GetTableClient("articles").QueryAsync<TableEntity>(o => o.PartitionKey == nonExistentKey).ToListAsync(),
      client.GetTableClient("events").QueryAsync<TableEntity>(o => o.PartitionKey == nonExistentKey).ToListAsync(),
      client.GetTableClient("users").QueryAsync<TableEntity>(o => o.PartitionKey == nonExistentKey).ToListAsync(),
      client.GetTableClient("recipients").QueryAsync<TableEntity>(o => o.PartitionKey == nonExistentKey).ToListAsync()
    };
    await Task.WhenAll(tasks);
  }

  public async Task<User> GetUserAsync(string username)
  {
    var table = client.GetTableClient("users");
    var result = await table.GetEntityIfExistsAsync<User>(domain, username);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<User>> ListUsersAsync()
  {
    var table = client.GetTableClient("users");
    return await table.QueryAsync<User>(o => o.PartitionKey == domain).ToListAsync();
  }

  public async Task CreateUserAsync(User user)
  {
    var table = client.GetTableClient("users");
    await table.AddEntityAsync(user);
  }

  public async Task DeleteUserAsync(string username)
  {
    var table = client.GetTableClient("users");
    await table.DeleteEntityAsync(domain, username);
  }

  public async Task<Newsletter> GetNewsletterAsync(string date)
  {
    var table = client.GetTableClient("newsletters");
    var result = await table.GetEntityIfExistsAsync<Newsletter>(domain, date);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<Newsletter>> ListNewslettersAsync()
  {
    var table = client.GetTableClient("newsletters");
    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return await table.QueryAsync<Newsletter>(o => o.PartitionKey == domain && o.RowKey.CompareTo(today) >= 0).ToListAsync();
  }

  public async Task CreateNewsletterAsync(Newsletter newsletter)
  {
    var table = client.GetTableClient("newsletters");
    await table.AddEntityAsync(newsletter);
  }

  public async Task DeleteNewsletterAsync(string date)
  {
    var table = client.GetTableClient("newsletters");
    await table.DeleteEntityAsync(domain, date);
  }

  public async Task UpdateNewsletterAsync(Newsletter newsletter)
  {
    var table = client.GetTableClient("newsletters");
    await table.UpdateEntityAsync(newsletter, ETag.All, TableUpdateMode.Replace);
  }

  public async Task<Article> GetArticleAsync(string key)
  {
    var table = client.GetTableClient("articles");
    var result = await table.GetEntityIfExistsAsync<Article>(domain, key);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<Article>> ListArticlesAsync(string date = null)
  {
    var table = client.GetTableClient("articles");

    if (date is null)
    {
      var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      return await table.QueryAsync<Article>(o => o.PartitionKey == domain && o.RowKey.CompareTo(today) >= 0).ToListAsync();
    }
    var nextDay = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return await table.QueryAsync<Article>(o => o.PartitionKey == domain && o.RowKey.CompareTo(date) >= 0 && o.RowKey.CompareTo(nextDay) < 0).ToListAsync();
  }

  public async Task CreateArticleAsync(Article article)
  {
    var table = client.GetTableClient("articles");
    await table.AddEntityAsync(article);
  }

  public async Task DeleteArticleAsync(string key)
  {
    var table = client.GetTableClient("articles");
    await table.DeleteEntityAsync(domain, key);
  }

  public async Task UpdateArticleAsync(Article article)
  {
    ArgumentNullException.ThrowIfNull(article);
    var table = client.GetTableClient("articles");
    var resp = await table.UpdateEntityAsync(article, article.ETag, TableUpdateMode.Replace);
    article.ETag = resp.Headers.ETag.Value;
  }

  public async Task<CalendarEvent> GetEventAsync(string key)
  {
    var table = client.GetTableClient("events");
    var result = await table.GetEntityIfExistsAsync<CalendarEvent>(domain, key);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<CalendarEvent>> ListEventsAsync()
  {
    var table = client.GetTableClient("events");
    var twoWeeksAgo = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return (await table.QueryAsync<CalendarEvent>(o => o.PartitionKey == domain && o.RowKey.CompareTo(twoWeeksAgo) >= 0).ToListAsync())
      .Where(o => string.CompareOrdinal(o.EndDate, today) > 0).ToList();
  }

  public async Task CreateEventAsync(CalendarEvent ev)
  {
    var table = client.GetTableClient("events");
    await table.AddEntityAsync(ev);
  }

  public async Task ApproveEventAsync(string key)
  {
    var table = client.GetTableClient("events");
    await table.UpdateEntityAsync(new CalendarEvent { PartitionKey = domain, RowKey = key, IsApproved = true }, ETag.All, TableUpdateMode.Merge);
  }

  public async Task DeleteEventAsync(string key)
  {
    var table = client.GetTableClient("events");
    await table.DeleteEntityAsync(domain, key);
  }

  public async Task MoveArticleAsync(string originalArticleKey, Article newArticle, Newsletter source, Newsletter dest)
  {
    ArgumentNullException.ThrowIfNull(newArticle);
    var articlesTable = client.GetTableClient("articles");
    newArticle.PartitionKey = domain;
    var articlesBatch = new List<TableTransactionAction>() {
      new(TableTransactionActionType.Add, newArticle),
      new(TableTransactionActionType.Delete, new Article { PartitionKey = domain, RowKey = originalArticleKey }, ETag.All)
    };
    await articlesTable.SubmitTransactionAsync(articlesBatch);

    var newslettersTable = client.GetTableClient("newsletters");
    var newslettersBatch = new List<TableTransactionAction>() {
      new(TableTransactionActionType.UpdateReplace, source, ETag.All),
      new(TableTransactionActionType.UpdateReplace, dest, ETag.All)
    };
    await newslettersTable.SubmitTransactionAsync(newslettersBatch);
  }

  public async Task<int> CountRecipientsAsync()
  {
    var table = client.GetTableClient("recipients");
    return await table.QueryAsync<Recipient>(o => o.PartitionKey == domain, select: selectPartitionKey).CountAsync();
  }

  public async Task<IList<string>> ListRecipientsAsync()
  {
    var table = client.GetTableClient("recipients");
    var result = await table.QueryAsync<Recipient>(o => o.PartitionKey == domain, select: selectRowKey).ToListAsync();
    return result.Select(o => o.RowKey).ToList();
  }

  public async Task ReplaceRecipientsAsync(IList<string> recipients)
  {
    ArgumentNullException.ThrowIfNull(recipients);
    recipients = recipients.Select(o => o.Trim().ToLowerInvariant()).Where(o => o.Contains('@', StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
    var table = client.GetTableClient("recipients");
    var existing = await table.QueryAsync<Recipient>(o => o.PartitionKey == domain).ToListAsync();
    var existingHashSet = new HashSet<string>(existing.Select(o => o.RowKey), StringComparer.OrdinalIgnoreCase);
    var newHashSet = new HashSet<string>(recipients, StringComparer.OrdinalIgnoreCase);

    var allOperations = new List<TableTransactionAction>();
    foreach (var newRecipient in recipients.Where(o => !existingHashSet.Contains(o)))
    {
      allOperations.Add(new TableTransactionAction(TableTransactionActionType.Add, new Recipient { PartitionKey = domain, RowKey = newRecipient }));
    }
    foreach (var existingRecipient in existing.Where(o => !newHashSet.Contains(o.RowKey)))
    {
      allOperations.Add(new TableTransactionAction(TableTransactionActionType.Delete, new Recipient { PartitionKey = domain, RowKey = existingRecipient.RowKey }, ETag.All));
    }

    var batches = allOperations.Select((o, i) => new { Index = i, Value = o }).GroupBy(o => o.Index / 100).Select(o => o.Select(v => v.Value).ToList()).ToList();

    foreach (var batch in batches)
    {
      await table.SubmitTransactionAsync(batch);
    }
  }

  public async Task ReplaceUsersAsync(IList<string> csvUsers)
  {
    ArgumentNullException.ThrowIfNull(csvUsers);
    var table = client.GetTableClient("users");
    var existing = await table.QueryAsync<User>(o => o.PartitionKey == domain).ToListAsync();
    var existingHashSet = new HashSet<string>(existing.Select(o => $"{o.RowKey},{o.FirstName},{o.DisplayName}"), StringComparer.OrdinalIgnoreCase);
    var newHashSet = new HashSet<string>(csvUsers, StringComparer.OrdinalIgnoreCase);

    var deleteOperations = new List<TableTransactionAction>();
    foreach (var existingUser in existingHashSet.Where(o => !newHashSet.Contains(o)))
    {
      deleteOperations.Add(new TableTransactionAction(TableTransactionActionType.Delete, new User { PartitionKey = domain, RowKey = existingUser.Split(',')[0] }, ETag.All));
    }

    var insertOperations = new List<TableTransactionAction>();
    foreach (var newUser in csvUsers.Where(o => !existingHashSet.Contains(o)))
    {
      var parts = newUser.Split(',');
      insertOperations.Add(new TableTransactionAction(TableTransactionActionType.Add, new User
      {
        PartitionKey = domain,
        RowKey = parts[0],
        FirstName = parts[1],
        DisplayName = parts[2],
        IsEditor = false
      }));
    }

    var deleteBatches = deleteOperations.Select((o, i) => new { Index = i, Value = o }).GroupBy(o => o.Index / 100).Select(o => o.Select(v => v.Value).ToList()).ToList();
    var insertBatches = insertOperations.Select((o, i) => new { Index = i, Value = o }).GroupBy(o => o.Index / 100).Select(o => o.Select(v => v.Value).ToList()).ToList();

    foreach (var batch in deleteBatches.Concat(insertBatches))
    {
      await table.SubmitTransactionAsync(batch);
    }
  }
}

public static class QueryExtensions
{
  public static async Task<List<T>> ToListAsync<T>(this AsyncPageable<T> query)
  {
    ArgumentNullException.ThrowIfNull(query);
    var list = new List<T>();
    await foreach (var item in query)
    {
      list.Add(item);
    }
    return list;
  }

  public static async Task<int> CountAsync<T>(this AsyncPageable<T> query)
  {
    ArgumentNullException.ThrowIfNull(query);
    var count = 0;
    await foreach (var item in query)
    {
      count++;
    }
    return count;
  }
}
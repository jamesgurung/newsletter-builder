using Azure;
using Azure.Data.Tables;
using NewsletterBuilder.Entities;
using System.Globalization;

namespace NewsletterBuilder;

public class TableService
{
  private readonly TableServiceClient _client;
  private readonly string _domain;

  public TableService(TableServiceClient client, string domain)
  {
    _client = client;
    _domain = domain;
  }

  public async Task<User> GetUserAsync(string username)
  {
    var table = _client.GetTableClient("users");
    var result = await table.GetEntityIfExistsAsync<User>(_domain, username);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<User>> ListUsersAsync()
  {
    var table = _client.GetTableClient("users");
    return await table.QueryAsync<User>(o => o.PartitionKey == _domain).ToListAsync();
  }

  public async Task CreateUserAsync(User user)
  {
    var table = _client.GetTableClient("users");
    await table.AddEntityAsync(user);
  }

  public async Task DeleteUserAsync(string username)
  {
    var table = _client.GetTableClient("users");
    await table.DeleteEntityAsync(_domain, username);
  }

  public async Task<Newsletter> GetNewsletterAsync(string date)
  {
    var table = _client.GetTableClient("newsletters");
    var result = await table.GetEntityIfExistsAsync<Newsletter>(_domain, date);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<Newsletter>> ListNewslettersAsync()
  {
    var table = _client.GetTableClient("newsletters");
    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return await table.QueryAsync<Newsletter>(o => o.PartitionKey == _domain && o.RowKey.CompareTo(today) >= 0).ToListAsync();
  }

  public async Task CreateNewsletterAsync(Newsletter newsletter)
  {
    var table = _client.GetTableClient("newsletters");
    await table.AddEntityAsync(newsletter);
  }

  public async Task DeleteNewsletterAsync(string date)
  {
    var table = _client.GetTableClient("newsletters");
    await table.DeleteEntityAsync(_domain, date);
  }

  public async Task UpdateNewsletterAsync(Newsletter newsletter)
  {
    var table = _client.GetTableClient("newsletters");
    await table.UpdateEntityAsync(newsletter, ETag.All, TableUpdateMode.Replace);
  }

  public async Task<Article> GetArticleAsync(string key)
  {
    var table = _client.GetTableClient("articles");
    var result = await table.GetEntityIfExistsAsync<Article>(_domain, key);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<Article>> ListArticlesAsync(string date = null)
  {
    var table = _client.GetTableClient("articles");

    if (date is null)
    {
      var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      return await table.QueryAsync<Article>(o => o.PartitionKey == _domain && o.RowKey.CompareTo(today) >= 0).ToListAsync();
    }
    var nextDay = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return await table.QueryAsync<Article>(o => o.PartitionKey == _domain && o.RowKey.CompareTo(date) >= 0 && o.RowKey.CompareTo(nextDay) < 0).ToListAsync();
  }

  public async Task CreateArticleAsync(Article article)
  {
    var table = _client.GetTableClient("articles");
    await table.AddEntityAsync(article);
  }

  public async Task DeleteArticleAsync(string key)
  {
    var table = _client.GetTableClient("articles");
    await table.DeleteEntityAsync(_domain, key);
  }

  public async Task UpdateArticleAsync(Article article)
  {
    var table = _client.GetTableClient("articles");
    await table.UpdateEntityAsync(article, ETag.All, TableUpdateMode.Replace);
  }

  public async Task<CalendarEvent> GetEventAsync(string key)
  {
    var table = _client.GetTableClient("events");
    var result = await table.GetEntityIfExistsAsync<CalendarEvent>(_domain, key);
    return result.HasValue ? result.Value : null;
  }

  public async Task<List<CalendarEvent>> ListEventsAsync()
  {
    var table = _client.GetTableClient("events");
    var twoWeeksAgo = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    return (await table.QueryAsync<CalendarEvent>(o => o.PartitionKey == _domain && o.RowKey.CompareTo(twoWeeksAgo) >= 0).ToListAsync())
      .Where(o => string.CompareOrdinal(o.EndDate, today) > 0).ToList();
  }

  public async Task CreateEventAsync(CalendarEvent ev)
  {
    var table = _client.GetTableClient("events");
    await table.AddEntityAsync(ev);
  }

  public async Task ApproveEventAsync(string key)
  {
    var table = _client.GetTableClient("events");
    await table.UpdateEntityAsync(new CalendarEvent { PartitionKey = _domain, RowKey = key, IsApproved = true }, ETag.All, TableUpdateMode.Merge);
  }

  public async Task DeleteEventAsync(string key)
  {
    var table = _client.GetTableClient("events");
    await table.DeleteEntityAsync(_domain, key);
  }

  public async Task MoveArticleAsync(string originalArticleKey, Article newArticle, Newsletter source, Newsletter dest)
  {
    ArgumentNullException.ThrowIfNull(newArticle);
    var articlesTable = _client.GetTableClient("articles");
    newArticle.PartitionKey = _domain;
    var articlesBatch = new List<TableTransactionAction>() {
      new TableTransactionAction(TableTransactionActionType.Add, newArticle),
      new TableTransactionAction(TableTransactionActionType.Delete, new Article { PartitionKey = _domain, RowKey = originalArticleKey }, ETag.All)
    };
    await articlesTable.SubmitTransactionAsync(articlesBatch);

    var newslettersTable = _client.GetTableClient("newsletters");
    var newslettersBatch = new List<TableTransactionAction>() {
      new TableTransactionAction(TableTransactionActionType.UpdateReplace, source, ETag.All),
      new TableTransactionAction(TableTransactionActionType.UpdateReplace, dest, ETag.All)
    };
    await newslettersTable.SubmitTransactionAsync(newslettersBatch);
  }

  public async Task<int> CountRecipientsAsync()
  {
    var table = _client.GetTableClient("recipients");
    return await table.QueryAsync<Recipient>(o => o.PartitionKey == _domain, select: new[] { "PartitionKey" }).CountAsync();
  }

  public async Task<IList<string>> ListRecipientsAsync()
  {
    var table = _client.GetTableClient("recipients");
    var result = await table.QueryAsync<Recipient>(o => o.PartitionKey == _domain, select: new[] { "RowKey" }).ToListAsync();
    return result.Select(o => o.RowKey).ToList();
  }

  public async Task ReplaceRecipientsAsync(IList<string> recipients)
  {
    ArgumentNullException.ThrowIfNull(recipients);
    recipients = recipients.Select(o => o.Trim().ToLowerInvariant()).Where(o => o.Contains('@', StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
    var table = _client.GetTableClient("recipients");
    var existing = await _client.GetTableClient("recipients").QueryAsync<Recipient>(o => o.PartitionKey == _domain).ToListAsync();
    var existingHashSet = new HashSet<string>(existing.Select(o => o.RowKey), StringComparer.OrdinalIgnoreCase);
    var newHashSet = new HashSet<string>(recipients, StringComparer.OrdinalIgnoreCase);

    var allOperations = new List<TableTransactionAction>();
    foreach (var newRecipient in recipients.Where(o => !existingHashSet.Contains(o)))
    {
      allOperations.Add(new TableTransactionAction(TableTransactionActionType.Add, new Recipient { PartitionKey = _domain, RowKey = newRecipient }));
    }
    foreach (var existingRecipient in existing.Where(o => !newHashSet.Contains(o.RowKey)))
    {
      allOperations.Add(new TableTransactionAction(TableTransactionActionType.Delete, new Recipient { PartitionKey = _domain, RowKey = existingRecipient.RowKey }, ETag.All));
    }

    var batches = allOperations.Select((o, i) => new { Index = i, Value = o })
      .GroupBy(o => o.Index / 100)
      .Select(o => o.Select(v => v.Value).ToList())
      .ToList();

    foreach (var batch in batches)
    {
      await table.SubmitTransactionAsync(batch);
    }
  }
}

public static class QueryExtensions {
  public static async Task<List<T>> ToListAsync<T>(this AsyncPageable<T> query) {
    ArgumentNullException.ThrowIfNull(query);
    var list = new List<T>();  
    await foreach (var item in query)
    {
      list.Add(item);
    }
    return list;
  }

  public static async Task<int> CountAsync<T>(this AsyncPageable<T> query) {
    ArgumentNullException.ThrowIfNull(query);
    var count = 0;  
    await foreach (var item in query)
    {
      count++;
    }
    return count;
  }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NewsletterBuilder;

public interface IChatClient
{
  Task Type(string token);
  Task SendProgress(int perc);
}

[Authorize]
public class ChatHub : Hub<IChatClient> { }
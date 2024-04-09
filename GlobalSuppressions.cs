using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Not needed in an ASP.NET Core project.")]
[assembly: SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Some strings are stored in lowercase.")]
[assembly: SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "Not a library project.")]
[assembly: SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Intentionally string.")]
[assembly: SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Used in DTO classes.")]
[assembly: SuppressMessage("Usage", "CA2234:Pass system uri objects instead of strings", Justification = "HttpClient uses partial path")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Incorrect compiler warning", Scope = "member", Target = "~M:NewsletterBuilder.ChatGPT.SendGptRequestAsync(System.Collections.Generic.List{NewsletterBuilder.ChatGPTMessage},System.Decimal,System.String)~System.Threading.Tasks.Task{System.String}")]
[assembly: SuppressMessage("Quality", "CA1016:Mark assemblies with assembly version", Justification = "Not needed")]

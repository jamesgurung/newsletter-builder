﻿using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Quality", "CA1016:Mark assemblies with assembly version", Justification = "Not needed")]
[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Intentional")]
[assembly: SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Intentionally string")]
[assembly: SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Some strings are stored in lowercase")]
[assembly: SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Not a library")]
[assembly: SuppressMessage("Performance", "CA1812:Avoid uninstantiated public classes", Justification = "Instantiated through JSON deserialisation")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Consistent API", Scope = "member", Target = "~M:NewsletterBuilder.BlobService.GetSasQueryString~System.String")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Consistent API", Scope = "member", Target = "~M:NewsletterBuilder.TableService.CreateUserAsync(NewsletterBuilder.Entities.User)~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Consistent API", Scope = "type", Target = "~T:NewsletterBuilder.BlobService")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Consistent API", Scope = "type", Target = "~T:NewsletterBuilder.TableService")]
[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "Readability")]
[assembly: SuppressMessage("Performance", "CA1852:Seal public types", Justification = "Not a library project")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "Not expensive")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Not needed in an ASP.NET Core project")]
[assembly: SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Used in DTO classes")]
[assembly: SuppressMessage("Usage", "CA2234:Pass system uri objects instead of strings", Justification = "HttpClient uses partial path")]
[assembly: SuppressMessage("Style", "IDE0022:Use block body for method", Justification = "Readability")]
[assembly: SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Readability", Scope = "member", Target = "~P:NewsletterBuilder.Entities.CalendarEvent.DisplayDate")]
[assembly: SuppressMessage("Style", "IDE0055:Fix formatting", Justification = "Incorrect formatting recommended", Scope = "member", Target = "~M:NewsletterBuilder.Pages.PublishPageModel.OnGet(System.String)~System.Threading.Tasks.Task{Microsoft.AspNetCore.Mvc.IActionResult}")]
[assembly: SuppressMessage("Style", "IDE0058:Expression value is never used", Justification = "Expression values not required")]
[assembly: SuppressMessage("Style", "IDE0305:Simplify collection initialization", Justification = "Readability")]

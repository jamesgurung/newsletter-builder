# Newsletter Builder

This is a web app which allows multiple users to contribute articles to a school newsletter.

## Setup

In this guide we will use the following placeholders:
* `<your-email-domain>` - the domain of your user email addresses, e.g. `example.com`
* `<your-newsletter-domain>` - the domain where your newsletter static website will be available via the Azure CDN, e.g. `newsletter.example.com`
* `<your-newsletter-builder-domain>` - the domain where the newsletter builder app will be hosted, e.g. `build.newsletter.example.com`

Here are the steps:

1. Create a general purpose v2 storage account in Microsoft Azure.
    * Add a private container: `photos`
    * Add tables: `articles`, `events`, `newsletters`, `recipients`, `users`
    * The `users` table needs an initial entry, and this must be a valid user on your Azure tenant:
        * PartitionKey - `<your-email-domain>`
        * RowKey - `<username>`
        * IsEditor - `true`
        * FirstName - `<your-first-name>`
        * DisplayName - `<your-title-and-surname>`
    * Enable static website hosting, and set the index document to `index.html` and the error document to `404.html`. This will create a `$web` blob container, which will be the root of the public-facing newsletter website.
    * Customise the contents of the `StaticWebsite` folder in this repository, for example replacing placeholders with their appropriate values. Then upload to the `$web` blob container. Also upload `logo.jpg` (250x250px), `logo-hd.jpg` (1200x1200px), `favicon.ico`, and `icon-192x192.png`.
    * Enable CORS for blob `GET` requests from `<your-newsletter-builder-domain>`.

2. Create an Azure CDN endpoint with the static website as its origin, and a custom domain set to `https://<your-newsletter-domain>`. Enable compression. Add the following rules:
    * If request protocol = `HTTP` then URL redirect Found (302), HTTPS 
    * If URL file extension = `jpg`, `png`, `pdf`, `css`, or `js` (lowercase) then modify response header: overwrite `Cache-Control` `max-age=31536000`, and then cache expiration: override `365` days.
    * If URL file extension = `json`, `html`, or `txt` (lowercase) then modify response header: overwrite `Cache-Control` `no-cache`, and then cache expiration: bypass cache.
    * If URL path does not contain `.` then modify response header: overwrite `Cache-Control` `no-cache`, and then cache expiration: bypass cache.

3. Create an Azure app registration.
    * Name - `Newsletter Builder`
    * Redirect URI - `https://<your-newsletter-builder-domain>/signin-oidc`
    * Implicit grant - `ID tokens`
    * Supported account types - `Accounts in this organizational directory only`
    * API permissions - `Microsoft Graph - User.Read`
    * Token configuration - add optional claim of type `ID`: `upn`

4. Create an Azure OpenAI Service GPT-4 deployment.

5. Create a Postmark server.

6. Create an Azure App Service web app, and configure the following settings:
    * `NewsletterEditorUrl` - the URL of the newsletter editor (`https://<your-newsletter-builder-domain>`)
    * `Organisations__0__Name` - the name of your organisation
    * `Organisations__0__NewsletterUrl` - the URL of the published newsletter (`https://<your-newsletter-domain>`)
    * `Organisations__0__Address` - the address of your organisation
    * `Organisations__0__TwitterHandle` - the Twitter handle of your organisation, starting with an @ symbol
    * `Organisations__0__Footer` - the footer text to include in emails
    * `Organisations__0__BannedWords` - a JSON array of words which are not allowed in articles
    * `Organisations__0__PhotoConsentUrl` - the URL of the photo consent spreadsheet
    * `Organisations__0__FromEmail` - the email address from which newsletters and reminders will be sent
    * `Organisations__0__QualityAssuranceEmail` - the email address to which quality assurance requests will be sent
    * `Organisations__0__ReminderReplyTo` - the reply-to address for reminders
    * `Organisations__0__DefaultDeadlineDaysBeforePublish` - the default number of days before the publish date that articles are due
    * `Organisations__0__AzureStorageStaticWebsiteAccountName` - the account name of the storage account where the newsletter static website is hosted
    * `Organisations__0__AzureStorageStaticWebsiteAccountKey` - the account key of the storage account where the newsletter static website is hosted
    * `Organisations__0__Reminders__0__DaysBeforeDeadline` - the number of days before the publish date to send the first reminder (subsequent reminders can be set up by adding additional items with incrementing indices)
    * `Organisations__0__Reminders__0__Domain` - the domain for which this reminder applies (`<your-email-domain>`)
    * `Organisations__0__Reminders__0__Message` - the message to include in the reminder email
    * `Organisations__0__Reminders__0__Subject` - the subject of the reminder email
    * `Azure__ClientId` - the client ID of your Azure app registration
    * `Azure__TenantId` - your Azure tenant ID
    * `Azure__StorageAccountName` - the name of your Azure Storage account
    * `Azure__StorageAccountKey` - the key for your Azure Storage account
    * `Azure__OpenAIEndpoint` - the endpoint of your Azure OpenAI Service GPT-4 deployment
    * `Azure__OpenAIKey` - the key for your Azure OpenAI Service API
    * `PostmarkServerToken` - the token for your Postmark server
    * `AutomationApiKey` - a secret GUID which is used to authenticate requests to the automation API

7. Deploy to the App Service web app you created.

8. Create scheduled tasks to call the `/emailreminders/<your-email-domain>/<n>` endpoints for each reminder you configured, where `<n>` is the index of the reminder.
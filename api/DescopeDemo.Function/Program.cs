// ============================================================
// Program.cs — Application startup and dependency injection
//
// How it works:
//   Azure Functions (isolated worker model) has a standard .NET generic host.
//   We configure two things here:
//
//   1. ASP.NET Core integration (ConfigureFunctionsWebApplication)
//      Lets the function endpoints use familiar ASP.NET types like HttpRequest
//      and IActionResult instead of the Functions-specific HttpRequestData.
//
//   2. A singleton BlobServiceClient authenticated via DefaultAzureCredential.
//      DefaultAzureCredential automatically picks the right credential source:
//        - Local development: your "az login" Azure CLI session
//        - Deployed to Azure: the Function App's System-Assigned Managed Identity
//      Because it's a singleton, the credential chain and token cache are
//      initialized once at startup and reused for every request — not rebuilt
//      on each call to DocumentsFunction.
// ============================================================

using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

// Enable ASP.NET Core integration so that function classes can use HttpRequest
// and IActionResult (the standard ASP.NET types) instead of the lower-level
// Azure Functions HttpRequestData / HttpResponseData types.
builder.ConfigureFunctionsWebApplication();

// Wire up Application Insights for telemetry.
// Sampling behaviour is configured in host.json:
//   "excludedTypes: Request" → every HTTP request is always logged in full.
//   All other telemetry (dependency calls, traces) is sampled to control cost.
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// ---------- Keyless blob storage via Managed Identity ----------
//
// DefaultAzureCredential tries credential sources in order until one works:
//   1. Environment variables (not used here)
//   2. Azure CLI ("az login") — used during local development
//   3. Managed Identity — used when deployed to Azure
//
// The storage account name is the only configuration value needed.
// No connection string. No account key. No SAS token.
//
// Registering as a singleton matters because DefaultAzureCredential internally
// acquires OAuth tokens and caches them. Creating a new instance per request
// would force a fresh token acquisition on every call.
var accountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME")
    ?? throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME is not set.");

builder.Services.AddSingleton(
    new BlobServiceClient(
        // Standard Azure Blob Storage endpoint URL — no keys in the URI.
        new Uri($"https://{accountName}.blob.core.windows.net"),
        new DefaultAzureCredential()));

// CORS note:
//   Local dev  → "Host.CORS": "*" in local.settings.json handles it.
//   Azure      → configure allowed origins in the Function App's CORS blade.
//   No code change needed for either environment.

builder.Build().Run();

// ============================================================
// DocumentsFunction.cs — HTTP endpoints for the document portal
//
// How it works (end-to-end for every request):
//
//   Browser                  Azure Function              Azure Blob Storage
//   ──────                   ──────────────              ──────────────────
//   GET /api/documents ──►  1. Extract Bearer token
//   Authorization:           2. Validate JWT via OIDC
//   Bearer <Descope JWT>     3. Read "roles" claim
//                            4. Map role → container
//                            5. Call Blob Storage ───►  docs-readonly
//                            6. Return file list  ◄───  (Storage Blob Data Reader)
//
// The function never touches a storage account key or SAS token.
// Azure RBAC on the two containers is the authoritative enforcement layer:
//   docs-readonly  — Managed Identity has Storage Blob Data Reader
//   docs-readwrite — Managed Identity has Storage Blob Data Contributor
//
// If the application code got the container name wrong, Azure would still deny
// the operation. The role check here is defense-in-depth, not the sole gate.
// ============================================================

using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DescopeDemo.Function;

/// <summary>
/// Azure Functions HTTP endpoints for the document portal.
///
/// Auth flow for every request:
///   1. React frontend sends a Descope JWT in the Authorization: Bearer header.
///   2. <see cref="AuthenticateAsync"/> validates it via OIDC discovery (no static keys).
///   3. <see cref="DescopeJwtValidator.GetRole"/> reads the role from the token claims.
///   4. Each endpoint selects the appropriate Azure Blob container based on role:
///        - "viewer"   → docs-readonly   (Storage Blob Data Reader RBAC assigned)
///        - "uploader" → docs-readwrite  (Storage Blob Data Contributor RBAC assigned)
///
/// Keyless storage access:
///   The injected <see cref="BlobServiceClient"/> is authenticated via Managed Identity
///   (<see cref="Azure.Identity.DefaultAzureCredential"/>). No storage keys or connection
///   strings exist anywhere in this application — Azure RBAC is the enforcement layer.
/// </summary>
public class DocumentsFunction
{
    private readonly ILogger<DocumentsFunction> _log;
    private readonly string _projectId;

    // Injected singleton — see Program.cs for registration.
    // Using a singleton means DefaultAzureCredential's token cache is shared
    // across all requests rather than being rebuilt on every call.
    private readonly BlobServiceClient _blobService;

    /// <summary>
    /// Constructor called by the Azure Functions DI container.
    /// <paramref name="blobService"/> is the singleton registered in <c>Program.cs</c>,
    /// so <see cref="Azure.Identity.DefaultAzureCredential"/> is initialized once per
    /// application lifetime rather than on every request.
    /// </summary>
    public DocumentsFunction(ILogger<DocumentsFunction> log, BlobServiceClient blobService)
    {
        _log = log;
        _blobService = blobService;

        // Fail fast at startup if the required environment variable is missing,
        // rather than throwing a cryptic NullReferenceException on the first request.
        _projectId = Environment.GetEnvironmentVariable("DESCOPE_PROJECT_ID")
            ?? throw new InvalidOperationException("DESCOPE_PROJECT_ID is not set.");
    }

    // ============================================================
    // Helper: GetContainer
    // ============================================================

    /// <summary>
    /// Returns the container client for <paramref name="containerName"/> using the
    /// injected singleton <see cref="BlobServiceClient"/>.
    /// </summary>
    private BlobContainerClient GetContainer(string containerName) =>
        _blobService.GetBlobContainerClient(containerName);

    // ============================================================
    // Helper: AuthenticateAsync
    // ============================================================

    /// <summary>
    /// Validates the Bearer token in the request's Authorization header.
    /// </summary>
    /// <returns>
    /// <c>(principal, rawToken, null)</c> on success, or <c>(null, null, IActionResult)</c>
    /// containing a 401 response if the token is missing, malformed, or expired.
    /// </returns>
    private async Task<(System.Security.Claims.ClaimsPrincipal? principal, string? rawToken, IActionResult? error)>
        AuthenticateAsync(HttpRequest req)
    {
        // Every request must carry "Authorization: Bearer <jwt>".
        // The JWT is the Descope session token issued after the user signs in.
        var auth = req.Headers.Authorization.FirstOrDefault();
        if (auth == null || !auth.StartsWith("Bearer "))
            return (null, null, new UnauthorizedObjectResult("Missing Authorization header."));

        // Strip the "Bearer " prefix (7 characters) to get the raw JWT string.
        var token = auth["Bearer ".Length..];

        // Validate the JWT using Descope's OIDC public keys (see DescopeJwtValidator.cs).
        // Returns a ClaimsPrincipal (the authenticated user + their claims) on success,
        // or null if the token is invalid, expired, or tampered with.
        var principal = await DescopeJwtValidator.ValidateAsync(token, _projectId);
        if (principal == null)
            return (null, null, new UnauthorizedObjectResult("Invalid or expired token."));

        return (principal, token, null);
    }

    // ============================================================
    // GET /api/documents
    // ============================================================

    /// <summary>
    /// Lists all blobs visible to the authenticated user.
    ///
    /// Role-to-container mapping:
    ///   viewer   → docs-readonly   (read access only via Azure RBAC)
    ///   uploader → docs-readwrite  (read + write access via Azure RBAC)
    ///
    /// The container assignment here reflects the user's Descope role. Azure RBAC
    /// on the storage containers is the authoritative access control layer —
    /// application-level role checking is defense-in-depth.
    /// </summary>
    [Function("ListDocuments")]
    public async Task<IActionResult> ListDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequest req)
    {
        // Step 1 — Authenticate: validate the Descope JWT from the Authorization header.
        var (principal, rawToken, authError) = await AuthenticateAsync(req);
        if (authError != null) return authError; // 401 if token missing or invalid.

        // Step 2 — Authorize: read the tenant and role from the verified JWT's tenants claim.
        //   The tenant and role were assigned in the Descope Console and embedded in the token
        //   at sign-in time. No database call needed.
        var (tenantId, role) = DescopeJwtValidator.GetTenantAndRole(rawToken!);
        if (role == "none" || string.IsNullOrEmpty(tenantId))
            return new ObjectResult("No valid role assigned.") { StatusCode = 403 };

        // Step 3 — Select container based on tenant and role.
        //   Container names are prefixed with the tenant ID so each tenant's documents
        //   are isolated. Azure RBAC on the containers enforces the actual permission —
        //   even if this line were wrong, Azure would deny unauthorized operations.
        var containerName = role == "uploader" ? $"{tenantId}-readwrite" : $"{tenantId}-readonly";
        var container = GetContainer(containerName);

        // Step 4 — List blobs using the Managed Identity authenticated client.
        //   GetBlobsAsync() returns an async enumerable; we page through it building
        //   a lightweight list of name/size/date for the JSON response.
        var blobs = new List<object>();
        await foreach (var blob in container.GetBlobsAsync())
        {
            blobs.Add(new
            {
                name = blob.Name,
                sizeBytes = blob.Properties.ContentLength,
                lastModified = blob.Properties.LastModified,
            });
        }

        // Step 5 — Return the list along with the tenant, role, and container name so the
        //   frontend can display them in the portal header.
        return new OkObjectResult(new { tenantId, role, container = containerName, documents = blobs });
    }

    // ============================================================
    // GET /api/documents/{name}/download
    // ============================================================

    /// <summary>
    /// Streams a single blob as a file download.
    ///
    /// The <paramref name="name"/> parameter is validated to reject path traversal
    /// attempts. Azure Blob Storage names support "/" natively, so a name like
    /// "../../other-container/secret.pdf" could reference unintended blobs if
    /// passed through unvalidated.
    /// </summary>
    [Function("DownloadDocument")]
    public async Task<IActionResult> DownloadDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{name}/download")] HttpRequest req,
        string name)
    {
        // Step 1 — Authenticate.
        var (_, rawToken, authError) = await AuthenticateAsync(req);
        if (authError != null) return authError;

        // Step 2 — Authorize.
        var (tenantId, role) = DescopeJwtValidator.GetTenantAndRole(rawToken!);
        if (role == "none" || string.IsNullOrEmpty(tenantId))
            return new ObjectResult("No valid role assigned.") { StatusCode = 403 };

        // Step 3 — Validate the blob name to prevent path traversal.
        //   Azure blob names can contain "/" which acts as a virtual directory
        //   separator. Without this check, a crafted name like "../../secret"
        //   could potentially reference blobs outside the intended container.
        //   All blobs in this demo are stored flat (no virtual directories),
        //   so we simply reject any name that contains path characters.
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            return new BadRequestObjectResult("Invalid blob name.");

        // Step 4 — Select the tenant-specific container the user is allowed to read from.
        var containerName = role == "uploader" ? $"{tenantId}-readwrite" : $"{tenantId}-readonly";
        var blobClient = GetContainer(containerName).GetBlobClient(name);

        // Step 5 — Stream the blob content directly to the HTTP response.
        //   DownloadStreamingAsync() returns the blob's content as a stream
        //   without buffering the entire file in memory — important for large files.
        //   FileStreamResult sets Content-Disposition: attachment so the browser
        //   prompts a "Save As" dialog rather than trying to display the file inline.
        var download = await blobClient.DownloadStreamingAsync();
        var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
        return new FileStreamResult(download.Value.Content, contentType)
        {
            FileDownloadName = name,
        };
    }

    // ============================================================
    // POST /api/documents/upload
    // ============================================================

    /// <summary>
    /// Accepts a file upload and stores it in the <c>docs-readwrite</c> container.
    /// Requires the "uploader" role.
    ///
    /// Blob naming:
    ///   The name comes from the <c>X-Blob-Name</c> request header. It is sanitized
    ///   with <see cref="Path.GetFileName"/> to strip any path components the client
    ///   might supply (e.g., "../../evil.sh" becomes "evil.sh").
    ///
    /// Overwrite behavior:
    ///   Uploads with <c>overwrite: false</c> to prevent clobbering existing files.
    ///   A 409 Conflict is returned if a blob with the same name already exists.
    /// </summary>
    [Function("UploadDocument")]
    public async Task<IActionResult> UploadDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequest req)
    {
        // Step 1 — Authenticate.
        var (_, rawToken, authError) = await AuthenticateAsync(req);
        if (authError != null) return authError;

        // Step 2 — Authorize: only uploaders may write to the blob container.
        //   Viewers are denied here even before we touch storage. Azure RBAC would
        //   also deny the write if this check were somehow bypassed.
        var (tenantId, role) = DescopeJwtValidator.GetTenantAndRole(rawToken!);
        if (role != "uploader" || string.IsNullOrEmpty(tenantId))
            return new ObjectResult("Upload requires the 'uploader' role.") { StatusCode = 403 };

        // Step 3 — Read and sanitize the client-supplied blob name.
        //   The frontend sends the original filename in the X-Blob-Name header.
        //   Path.GetFileName strips any directory components, so a malicious
        //   name like "../../etc/passwd" becomes just "passwd".
        //   If the header is missing entirely, we fall back to a timestamp name.
        var rawName = req.Headers.TryGetValue("X-Blob-Name", out var nameVals) && nameVals.Count > 0
            ? nameVals[0]!
            : $"upload-{DateTime.UtcNow:yyyyMMddHHmmss}.bin";

        var filename = Path.GetFileName(rawName);

        // Path.GetFileName returns "" if the input is only directory separators.
        if (string.IsNullOrWhiteSpace(filename))
            filename = $"upload-{DateTime.UtcNow:yyyyMMddHHmmss}.bin";

        // Step 4 — Upload the raw request body directly to the tenant-specific blob container.
        //   overwrite: false means Azure returns 409 Conflict if a blob with this
        //   name already exists, preventing accidental overwrites of existing files.
        //   The request body is streamed directly to storage without buffering the
        //   entire file in memory on the Function host.
        var containerName = $"{tenantId}-readwrite";
        var blobClient = GetContainer(containerName).GetBlobClient(filename);
        await blobClient.UploadAsync(req.Body, overwrite: false);

        // Step 5 — Log and return 201 Created with the stored blob name.
        _log.LogInformation("Uploaded {Filename} to {Container} by {Role}", filename, containerName, role);
        return new ObjectResult(new { uploaded = filename, container = containerName })
        {
            StatusCode = 201
        };
    }
}

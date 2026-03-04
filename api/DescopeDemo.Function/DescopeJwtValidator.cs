// ============================================================
// DescopeJwtValidator.cs
//
// How it works:
//   Descope is an OpenID Connect (OIDC) provider. Every user who signs in
//   receives a signed JWT (JSON Web Token). To verify the token without
//   shipping a static secret, we use OIDC discovery:
//
//   1. Call Descope's discovery URL to fetch its public signing keys:
//      https://api.descope.com/{PROJECT_ID}/.well-known/openid-configuration
//   2. Use those keys to verify the token's RSA signature.
//   3. Check that the issuer matches our Descope project.
//   4. Check that the token hasn't expired.
//   5. Read the "roles" claim out of the verified payload.
//
//   The signing keys are cached in a ConfigurationManager so we only call
//   Descope's discovery endpoint once (plus background refreshes every ~1h).
//   No Descope SDK, no static secrets, no database — just standard OIDC.
// ============================================================

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace DescopeDemo.Function;

/// <summary>
/// Validates Descope-issued JWTs and extracts authorization claims.
///
/// Descope acts as an OpenID Connect provider. This class fetches the public
/// signing keys from Descope's OIDC discovery endpoint and uses them to verify
/// the token's signature, issuer, and lifetime — no static secrets or Descope
/// SDK dependencies required.
///
/// The OIDC <see cref="ConfigurationManager{T}"/> is cached as a thread-safe
/// singleton so that signing keys are fetched once and automatically refreshed
/// per Microsoft.IdentityModel's default cache interval (approximately 1 hour).
/// </summary>
public static class DescopeJwtValidator
{
    // ---------- OIDC configuration cache ----------
    //
    // ConfigurationManager talks to Descope's discovery endpoint and caches the
    // response (signing keys, issuer URL, etc.). One instance per application is
    // enough — it refreshes keys automatically in the background.
    //
    // We use double-checked locking so that:
    //   - After the first initialization, every request takes the fast path
    //     (null check, no lock).
    //   - On cold start, only one thread actually creates the manager; all
    //     others wait and then reuse the single instance.
    private static IConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private static readonly object _configManagerLock = new();

    private static IConfigurationManager<OpenIdConnectConfiguration> GetConfigManager(string projectId)
    {
        // Fast path — already initialized; skip the lock on every subsequent call.
        if (_configManager is not null) return _configManager;

        // Slow path — first request. Only one thread creates the manager.
        lock (_configManagerLock)
        {
            // Re-check inside the lock in case another thread just finished initializing.
            _configManager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                // Descope's OIDC discovery URL. Returns JSON with signing key URLs,
                // issuer string, supported algorithms, etc.
                $"https://api.descope.com/{projectId}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }

        return _configManager;
    }

    /// <summary>
    /// Validates a Descope Bearer token using OIDC discovery.
    /// </summary>
    /// <param name="bearerToken">The raw JWT string (without the "Bearer " prefix).</param>
    /// <param name="projectId">The Descope project ID used to build the issuer URL.</param>
    /// <returns>
    /// A <see cref="ClaimsPrincipal"/> on success, or <c>null</c> if the token is
    /// invalid, expired, or has a bad signature. Infrastructure failures (network
    /// errors fetching OIDC config, etc.) are intentionally not caught here and
    /// will surface to callers as unhandled exceptions, resulting in a 500 response
    /// rather than a silent 401.
    /// </returns>
    public static async Task<ClaimsPrincipal?> ValidateAsync(string bearerToken, string projectId)
    {
        // Step 1 — Get Descope's current public signing keys.
        //   On first call this hits the discovery URL. On subsequent calls it returns
        //   the cached config (refreshed automatically every ~1 hour).
        var config = await GetConfigManager(projectId)
            .GetConfigurationAsync(CancellationToken.None);

        // Step 2 — Define what "valid" means for a token from our Descope project.
        var validationParams = new TokenValidationParameters
        {
            // The issuer is the Descope API URL for our specific project.
            // A token from a different Descope project would have a different
            // issuer and fail here, even if its signature were otherwise valid.
            ValidateIssuer = true,
            ValidIssuer = $"https://api.descope.com/{projectId}",

            // Descope doesn't set an audience claim by default in its JWTs,
            // so we skip audience validation.
            ValidateAudience = false,

            // Reject expired tokens. Descope tokens have a short lifetime (~1h).
            ValidateLifetime = true,

            // The public keys fetched from the discovery endpoint.
            // The JWT header specifies which key ID (kid) was used to sign it,
            // and the handler picks the matching key automatically.
            IssuerSigningKeys = config.SigningKeys,
        };

        try
        {
            // Step 3 — Cryptographically verify the JWT.
            //   ValidateToken parses the three base64 sections of the JWT
            //   (header.payload.signature), verifies the RSA signature, and
            //   returns a ClaimsPrincipal populated with all the JWT's claims.
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(bearerToken, validationParams, out _);
        }
        catch (SecurityTokenException)
        {
            // The token failed validation — it's malformed, expired, or signed
            // with the wrong key. Return null so callers respond with 401.
            return null;
        }
        // Note: we intentionally don't catch other exceptions (e.g., HttpRequestException
        // if Descope's discovery endpoint is unreachable). Those surface as 500 errors,
        // which is correct — a network failure is not the same as a bad token.
    }

    /// <summary>
    /// Extracts the highest-privilege role from the Descope JWT claims.
    ///
    /// Descope encodes roles as a JSON array in the <c>roles</c> claim, e.g.
    /// <c>"roles": ["uploader"]</c>. However, <see cref="JwtSecurityTokenHandler"/>
    /// maps the JWT <c>roles</c> claim name to the full .NET URI
    /// <c>ClaimTypes.Role</c> (<c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>).
    /// We union both claim names to handle either mapping safely.
    /// </summary>
    /// <returns>"uploader", "viewer", or "none" if no recognized role is present.</returns>
    public static string GetRole(ClaimsPrincipal principal)
    {
        // JwtSecurityTokenHandler silently renames well-known JWT claim names to
        // their .NET equivalents. The JWT claim "roles" becomes ClaimTypes.Role
        // (a long URI). We search both names so the code works regardless of
        // which claim type the handler used for a given token.
        var roles = principal.FindAll("roles")          // raw JWT claim name
            .Concat(principal.FindAll(ClaimTypes.Role)) // .NET mapped claim name
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Uploader takes precedence in case a user has both roles assigned.
        if (roles.Contains("uploader")) return "uploader";
        if (roles.Contains("viewer")) return "viewer";
        return "none"; // User is authenticated but has no recognized role.
    }

    /// <summary>
    /// Extracts tenant ID and role from the Descope JWT's <c>tenants</c> claim.
    ///
    /// Descope's multi-tenant JWTs include a <c>tenants</c> claim shaped like:
    /// <code>{ "hotel-a": { "roles": ["viewer"] } }</code>
    ///
    /// JwtSecurityTokenHandler does not flatten nested objects, so we re-parse
    /// the raw JWT string. This is safe — the token was already validated by
    /// <see cref="ValidateAsync"/>; we're only reading its payload here.
    /// </summary>
    /// <param name="rawToken">The validated JWT string (without "Bearer " prefix).</param>
    /// <returns>
    /// The first tenant ID found in the token plus the highest-privilege role
    /// within that tenant. Returns ("", "none") if no valid tenant/role is found.
    /// </returns>
    public static (string tenantId, string role) GetTenantAndRole(string rawToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(rawToken);

        if (!jwt.Payload.TryGetValue("tenants", out var tenantsValue) || tenantsValue == null)
            return ("", "none");

        // tenantsValue is an object deserialized from the JWT payload JSON.
        // Serialize it back to a JSON string so System.Text.Json can parse it cleanly.
        var tenantsJson = tenantsValue.ToString();
        if (string.IsNullOrWhiteSpace(tenantsJson) || tenantsJson == "{}")
            return ("", "none");

        Dictionary<string, JsonElement>? tenants;
        try
        {
            tenants = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tenantsJson);
        }
        catch (JsonException)
        {
            return ("", "none");
        }

        if (tenants == null || tenants.Count == 0)
            return ("", "none");

        // Demo assumption: a user belongs to exactly one tenant.
        // Take the first entry in the map.
        var (tenantId, tenantInfo) = tenants.First();

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tenantInfo.TryGetProperty("roles", out var rolesElement) &&
            rolesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rolesElement.EnumerateArray())
            {
                var val = r.GetString();
                if (val != null) roles.Add(val);
            }
        }

        string role = roles.Contains("uploader") ? "uploader"
                    : roles.Contains("viewer")   ? "viewer"
                    : "none";

        return (tenantId, role);
    }
}

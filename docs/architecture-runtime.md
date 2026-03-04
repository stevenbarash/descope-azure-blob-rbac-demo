# Runtime Flow

The tenant embedded in the Descope JWT routes the user to their organization's container.
The role determines whether they can upload.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'actorBkg': '#1e293b', 'actorBorder': '#0f172a', 'actorTextColor': '#f8fafc', 'actorLineColor': '#cbd5e1', 'signalColor': '#64748b', 'signalTextColor': '#334155', 'noteBkgColor': '#f8fafc', 'noteBorderColor': '#e2e8f0', 'noteTextColor': '#64748b', 'activationBkgColor': '#ede9fe', 'activationBorderColor': '#7c3aed', 'sequenceNumberColor': '#7c3aed'}}}%%
sequenceDiagram
    autonumber
    actor User
    participant Descope as Descope
    participant Func as Azure Function
    participant Blob as Blob Storage

    rect rgb(245, 243, 255)
        Note over User,Descope: Step 1 — Sign in
        User->>+Descope: Sign in (OTP / Magic Link)
        Note over Descope: Authenticates user, looks up tenant and role
        Descope-->>-User: JWT with tenants claim containing tenant ID and role
    end

    rect rgb(239, 246, 255)
        Note over User,Blob: Step 2 — Load documents
        User->>+Func: GET /api/documents  Bearer JWT
        Note over Func: Validate JWT via OIDC discovery
        Note over Func: Parse tenants claim — tenant ID + role
        Note over Func: container = tenant ID
        Func->>+Blob: List blobs in [tenant-id] container via Managed Identity
        Blob-->>-Func: File list
        Func-->>-User: tenantId, role, container, documents
    end

    rect rgb(240, 253, 244)
        Note over User,Blob: Step 3 — Upload (uploader role only)
        User->>+Func: POST /api/documents/upload  Bearer JWT
        Note over Func: Confirms role == uploader, else 403
        Func->>+Blob: Write file to [tenant-id] container via Managed Identity
        Blob-->>-Func: 201 Created
        Func-->>-User: uploaded, container
    end
```

**Tenant isolation:** users only ever access their own tenant's container. The tenant ID from the JWT is used as the container name — there is no path to another tenant's data.

**Role enforcement:** viewers and uploaders in the same tenant see the same files. Only uploaders can write. The Managed Identity has Storage Blob Data Contributor on all containers; viewer write attempts are blocked at the application layer before reaching storage.

**No static secrets:** the Azure Function authenticates to Blob Storage via Managed Identity (`DefaultAzureCredential`). JWT validation uses Descope's OIDC discovery endpoint — no Descope SDK, no stored keys.

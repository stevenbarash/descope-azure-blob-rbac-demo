# Runtime Flow

The JWT Descope issues at sign-in carries both the tenant ID and role. The Function reads them directly from the token — no database, no extra API call.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'actorBkg': '#1e293b', 'actorBorder': '#0f172a', 'actorTextColor': '#f8fafc', 'actorLineColor': '#cbd5e1', 'signalColor': '#475569', 'signalTextColor': '#1e293b', 'noteBkgColor': '#f8fafc', 'noteBorderColor': '#e2e8f0', 'noteTextColor': '#475569', 'activationBkgColor': '#dbeafe', 'activationBorderColor': '#1d4ed8'}}}%%
sequenceDiagram
    actor User
    participant D as Descope
    participant F as Azure Function
    participant B as Blob Storage

    rect rgb(245, 243, 255)
        Note over User,D: Step 1 — Sign in
        User->>+D: Authenticate (OTP or Magic Link)
        Note over D: Looks up tenant membership and role
        D-->>-User: JWT — contains tenant ID and role, signed by Descope
    end

    rect rgb(239, 246, 255)
        Note over User,B: Step 2 — Load documents
        User->>+F: GET /api/documents with Bearer JWT
        Note over F: Validates JWT signature via Descope OIDC endpoint
        Note over F: Reads tenant ID and role directly from token claims
        Note over F: Sets container = tenant ID
        F->>+B: List files in that container via Managed Identity
        B-->>-F: File list
        F-->>-User: Files, plus tenant ID and role for the portal header
    end

    rect rgb(240, 253, 244)
        Note over User,B: Step 3 — Upload (uploader role only)
        User->>+F: POST /api/documents/upload with Bearer JWT
        Note over F: Same JWT check — role must be uploader, else 403
        F->>+B: Write file to same container via Managed Identity
        B-->>-F: Saved
        F-->>-User: File appears in portal immediately
    end
```

**Tenant isolation** — each organization's files live in a separate container named after their Descope tenant ID. A user from org-a cannot access org-b's container because their JWT only contains org-a's tenant ID.

**Role enforcement** — viewers and uploaders in the same org see the same files. Write access is blocked in the Function before touching storage.

**No storage keys anywhere** — the Function talks to Blob Storage via `DefaultAzureCredential` (Managed Identity in Azure, `az login` locally). No connection strings, no SAS tokens.

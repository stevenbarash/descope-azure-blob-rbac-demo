# Descope + Azure Blob Storage RBAC Demo

A working demo showing how to combine [Descope](https://descope.com) multi-tenant authentication with Azure Blob Storage — no storage keys, no SAS tokens, no custom authorization middleware.

Users sign in with a Descope OTP flow. Their **tenant ID** and **role** are embedded in the JWT. An Azure Function validates the JWT, reads the tenant and role, and routes the request to the correct blob container via Managed Identity. Azure RBAC enforces what the identity can actually do.

---

## How it works

- Each **tenant** gets its own blob container (container name = Descope tenant ID).
- **Viewers** can list and download their tenant's files.
- **Uploaders** can also upload files to their tenant's container.
- **No storage keys or SAS tokens** exist anywhere in the application.
- **Multi-tenant isolation** is enforced at both the application and Azure RBAC layers.

---

## Architecture

```
Browser (React + Descope SDK)
  │
  │  Authorization: Bearer <Descope JWT>
  ▼
Azure Function (.NET 8, Isolated Worker)
  │  1. Validate JWT via OIDC discovery (no static secrets)
  │  2. Read tenant ID + role from "tenants" claim
  │  3. Select container = tenant ID
  │  4. Gate uploads to uploader role only
  ▼
Azure Blob Storage (Managed Identity — no keys)
  ├── <tenant-a>   ← Storage Blob Data Contributor on Managed Identity
  ├── <tenant-b>   ← Storage Blob Data Contributor on Managed Identity
  └── ...
```

The Function contains **no authorization logic** beyond reading the tenant/role from the verified JWT and selecting a container name. Azure RBAC on the storage containers is the authoritative enforcement layer.

### JWT structure

Descope embeds tenant membership in a `tenants` claim:

```json
{
  "tenants": {
    "hotel-a": {
      "roles": ["uploader"]
    }
  }
}
```

The Function reads the first tenant entry to get the container name and role. No database call required.

### Role behavior

| Role | Can list/download | Can upload |
|---|---|---|
| `viewer` | ✓ | ✗ (403) |
| `uploader` | ✓ | ✓ |

Both roles access the **same container** (their tenant's). Role only controls write access.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Auth | [Descope](https://descope.com) (OIDC, JWT, multi-tenant, roles) |
| Frontend | React 18, Vite, Tailwind CSS, `@descope/react-sdk` |
| API | .NET 8 Azure Functions (Isolated Worker) |
| Storage | Azure Blob Storage via `Azure.Identity` (`DefaultAzureCredential`) |
| JWT validation | `Microsoft.IdentityModel.Protocols.OpenIdConnect` |

---

## Prerequisites

- [Node.js](https://nodejs.org) v18+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` must work)
- A [Descope account](https://app.descope.com) (free tier is fine)
- An Azure subscription

---

## Part 1 — Descope Setup

### 1.1 Create a project

1. Go to [app.descope.com](https://app.descope.com) → **Create new project**
2. Copy your **Project ID** from **Project Settings** (top-left menu). It looks like `P2xxxxxxxxxxxxxxxx`.

### 1.2 Create tenants

Go to **Tenants → Create Tenant**. Create one tenant per organization. The **tenant ID** you set here must exactly match the blob container name you create in Azure.

Example:

| Tenant name | Tenant ID (must match Azure container) |
|---|---|
| Hotel A | `hotel-a` |
| Hotel B | `hotel-b` |

### 1.3 Create roles

Go to **Authorization → Roles → Create Role**:

| Role name | Description |
|---|---|
| `viewer` | Can list and download documents |
| `uploader` | Can list, download, and upload documents |

### 1.4 Create test users

Go to **User Management → Create User** and add users for each tenant and role:

| Email | Tenant | Role |
|---|---|---|
| `viewer@example.com` | `hotel-a` | `viewer` |
| `uploader@example.com` | `hotel-a` | `uploader` |

Assign the tenant and role in the **Roles** tab of each user's profile. Make sure to assign the role within the tenant context (not globally).

### 1.5 Configure the sign-in flow

Go to **Flows** and create or edit a flow with ID `sign-in-otp`. Add at least one authentication step:

- **Email OTP** — one-time code sent to email
- **Magic Link** — passwordless link sent to email
- **WhatsApp OTP** — requires a WhatsApp business account

---

## Part 2 — Azure Setup

### 2.1 Create a resource group

```bash
az group create --name rg-descope-demo --location eastus
```

### 2.2 Create a storage account

```bash
az storage account create \
  --name <YOUR_STORAGE_ACCOUNT_NAME> \
  --resource-group rg-descope-demo \
  --location eastus \
  --sku Standard_LRS \
  --allow-blob-public-access false
```

### 2.3 Create a container per tenant

The container name must match the Descope tenant ID exactly.

```bash
az storage container create \
  --name <TENANT_ID> \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --auth-mode login
```

Repeat for each tenant.

### 2.4 Seed sample files (optional)

```bash
az storage blob upload \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --container-name <TENANT_ID> \
  --name sample-doc.pdf \
  --file /path/to/sample-doc.pdf \
  --auth-mode login
```

### 2.5 Create the Function App with a Managed Identity

```bash
az functionapp create \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --storage-account <YOUR_STORAGE_ACCOUNT_NAME> \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux \
  --assign-identity [system]
```

### 2.6 Get the Managed Identity Object ID

```bash
az functionapp identity show \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --query principalId \
  --output tsv
```

### 2.7 Assign RBAC to the Managed Identity

Grant **Storage Blob Data Contributor** on each tenant container. This allows the Function to read and write; the application-level role check restricts viewers from uploading.

```bash
CONTAINER_ID=$(az storage container show \
  --name <TENANT_ID> \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --query id --output tsv)

az role assignment create \
  --assignee <MANAGED_IDENTITY_OBJECT_ID> \
  --role "Storage Blob Data Contributor" \
  --scope $CONTAINER_ID
```

Repeat for each container. RBAC propagation takes 1–2 minutes.

### 2.8 Configure the Function App settings

```bash
az functionapp config appsettings set \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --settings \
    DESCOPE_PROJECT_ID=<YOUR_DESCOPE_PROJECT_ID> \
    AZURE_STORAGE_ACCOUNT_URL=https://<YOUR_STORAGE_ACCOUNT_NAME>.blob.core.windows.net
```

### 2.9 Configure CORS

```bash
az functionapp cors add \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --allowed-origins "http://localhost:5173" "https://<YOUR_FRONTEND_DOMAIN>"
```

---

## Part 3 — Local Development

### 3.1 Clone and install

```bash
git clone https://github.com/stevenbarash/descope-azure-blob-rbac-demo.git
cd descope-azure-blob-rbac-demo
```

### 3.2 Configure the frontend

```bash
cd frontend
cp .env.example .env.local
```

Edit `.env.local`:

```
VITE_DESCOPE_PROJECT_ID=<YOUR_DESCOPE_PROJECT_ID>
```

```bash
npm install
```

### 3.3 Configure the API

Create `api/DescopeDemo.Function/local.settings.json` (gitignored):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DESCOPE_PROJECT_ID": "<YOUR_DESCOPE_PROJECT_ID>",
    "AZURE_STORAGE_ACCOUNT_URL": "https://<YOUR_STORAGE_ACCOUNT_NAME>.blob.core.windows.net"
  },
  "Host": {
    "CORS": "*"
  }
}
```

Locally, `DefaultAzureCredential` uses your `az login` credentials. Your CLI user needs **Storage Blob Data Contributor** on each tenant container (or **Storage Blob Data Owner** on the account).

### 3.4 Run the API locally

```bash
cd api/DescopeDemo.Function
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"   # macOS with Homebrew dotnet@8
func start
```

Expected output:

```
Functions:
    ListDocuments:    [GET]  http://localhost:7071/api/documents
    DownloadDocument: [GET]  http://localhost:7071/api/documents/{name}/download
    UploadDocument:   [POST] http://localhost:7071/api/documents/upload
```

### 3.5 Run the frontend

```bash
cd frontend
npm run dev
```

Vite proxies `/api/*` to `http://localhost:7071`.

---

## Part 4 — Deploy

### Deploy the Function App

```bash
cd api/DescopeDemo.Function
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"   # macOS only
func azure functionapp publish <YOUR_FUNCTION_APP_NAME> --dotnet-isolated
```

### Verify

```bash
# Should return 401 (no token)
curl -i https://<YOUR_FUNCTION_APP_NAME>.azurewebsites.net/api/documents
```

### Deploy the frontend (optional)

```bash
cd frontend
npm run build
# deploy dist/ to any static host
```

If deploying to Azure Static Web Apps or another domain, add it to **Descope Console → Project Settings → Security → Approved Domains**.

---

## Environment Variables

### API

| Variable | Description |
|---|---|
| `DESCOPE_PROJECT_ID` | Descope project ID (e.g. `P2xxxxxxxxxxxxxxxx`) |
| `AZURE_STORAGE_ACCOUNT_URL` | Storage account blob endpoint URL |

### Frontend

| Variable | Description |
|---|---|
| `VITE_DESCOPE_PROJECT_ID` | Same Descope project ID |

---

## Project Structure

```
.
├── api/
│   └── DescopeDemo.Function/
│       ├── DescopeJwtValidator.cs   # OIDC JWT validation + tenant/role extraction
│       ├── DocumentsFunction.cs     # HTTP endpoints: list, download, upload
│       ├── Program.cs               # DI: BlobServiceClient singleton
│       └── local.settings.json      # Local dev config (gitignored)
├── frontend/
│   ├── src/
│   │   ├── App.jsx                  # Auth gate: LoginPage vs Portal
│   │   ├── main.jsx                 # Descope AuthProvider root
│   │   ├── components/
│   │   │   ├── LoginPage.jsx        # Embedded Descope sign-in flow
│   │   │   ├── Portal.jsx           # Document list + download
│   │   │   └── UploadPanel.jsx      # File upload (uploader role only)
│   │   └── hooks/
│   │       └── useDocuments.js      # Fetches /api/documents with Bearer token
│   └── .env.example
└── docs/
    └── plans/                       # Implementation plans
```

---

## Demo Walkthrough

1. **Sign in as viewer** — document list loads, no upload panel, header shows tenant ID and `viewer` role
2. **Sign in as uploader** — upload panel appears; uploaded files appear in the list immediately
3. **Inspect the token** at [jwt.io](https://jwt.io) — find the `tenants` claim with tenant ID and role
4. **Prove API enforcement** (not just UI):
   ```js
   // Attempt upload as viewer — paste in browser DevTools
   fetch('/api/documents/upload', {
     method: 'POST',
     headers: {
       Authorization: `Bearer ${YOUR_VIEWER_TOKEN}`,
       'X-Blob-Name': 'test.txt'
     },
     body: new Blob(['test'])
   }).then(r => r.text()).then(console.log)
   // → "Upload requires the 'uploader' role."
   ```
5. **Add a second tenant** — create a new Descope tenant, a matching Azure container, and a user; sign in to confirm they see only their own files

---

## Security Notes

- **Path traversal protection** — `DownloadDocument` rejects blob names containing `/`, `\`, or `..`
- **Filename sanitization** — `UploadDocument` runs `Path.GetFileName()` on the client-supplied `X-Blob-Name` header
- **No overwrite** — uploads fail with 409 if a blob with the same name already exists
- **OIDC key caching** — signing keys fetched once, auto-refreshed; thread-safe via double-checked locking
- **Infrastructure errors surface as 500** — only `SecurityTokenException` returns 401; network failures fetching OIDC config are not silently swallowed as auth errors
- **No static secrets** — the only configuration values are the Descope project ID and the storage account URL

---

## License

MIT

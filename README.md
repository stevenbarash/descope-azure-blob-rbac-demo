# Descope + Azure Blob Storage RBAC Demo

A proof-of-concept showing how [Descope](https://descope.com) replaces handles the authentication layer while Azure's native RBAC continues to enforce blob storage access — with **zero storage keys, zero SAS tokens, and zero custom authorization code**.

Users log in via Descope. Their role is embedded in the Descope JWT. An Azure Function reads the role, routes the request to the correct blob container, and calls Azure Blob Storage via **Managed Identity**. Azure enforces what the identity can actually do on each container.

---

## Architecture

```
React Frontend (Vite)
    │
    │  Bearer <Descope JWT>
    ▼
Azure Function (.NET 8, Isolated Worker)
    │  1. Validate JWT via OIDC discovery
    │  2. Extract role from token claims
    │  3. Select container by role
    ▼
Azure Blob Storage  (DefaultAzureCredential — Managed Identity)
    ├── docs-readonly    ← viewers    (Storage Blob Data Reader)
    └── docs-readwrite   ← uploaders  (Storage Blob Data Contributor)
```

### Role mapping

| Descope Role | Container | Azure RBAC |
|---|---|---|
| `viewer` | `docs-readonly` | Storage Blob Data Reader |
| `uploader` | `docs-readwrite` | Storage Blob Data Contributor |

The Function contains **no authorization logic** beyond reading the role claim and selecting a container name. Azure enforces what the Managed Identity can actually do on each container — if a viewer somehow bypassed the Function and called storage directly, they would still be denied.

---

## What This Demo Proves

| Scenario | Result |
|---|---|
| Viewer logs in → `GET /api/documents` | Lists files from `docs-readonly` ✓ |
| Viewer calls `POST /api/documents/upload` | `403 Forbidden` — role mismatch ✗ |
| Uploader logs in → `GET /api/documents` | Lists files from `docs-readwrite` ✓ |
| Uploader calls `POST /api/documents/upload` | Uploads to `docs-readwrite` ✓ |
| Any request without a valid JWT | `401 Unauthorized` ✗ |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Auth | [Descope](https://descope.com) (OIDC, JWT, roles) |
| Frontend | React 18, Vite, Tailwind CSS, `@descope/react-sdk` |
| API | .NET 8 Azure Functions (Isolated Worker) |
| Storage | Azure Blob Storage via `Azure.Identity` (`DefaultAzureCredential`) |
| JWT validation | `Microsoft.IdentityModel.Protocols.OpenIdConnect` |

---

## Prerequisites

- [Node.js](https://nodejs.org) v18+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (`npm install -g azure-functions-core-tools@4`)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` must work)
- A [Descope account](https://app.descope.com) (free tier is fine)
- An Azure subscription with permissions to create resources

---

## Part 1 — Descope Setup

### 1.1 Create a project

1. Go to [app.descope.com](https://app.descope.com) → **Create new project**
2. Note your **Project ID** from **Project Settings** (top-left menu). It looks like `P2xxxxxxxxxxxxxxxx`.

### 1.2 Create roles

Go to **Authorization → Roles → Create Role** and add:

| Role name | Description |
|---|---|
| `viewer` | Can view and download documents |
| `uploader` | Can view, download, and upload documents |

### 1.3 Create test users

Go to **User Management → Create User** and add two users:

| Email | Role |
|---|---|
| `viewer@example.com` | `viewer` |
| `uploader@example.com` | `uploader` |

> You can use any email addresses. Each user must have the role assigned in the **Roles** tab of their user profile.

### 1.4 Configure the sign-in flow

Go to **Flows → sign-in** and ensure at least one of these steps is enabled:

- **Email OTP** — one-time code sent to email
- **Magic Link** — passwordless link sent to email
- **WhatsApp OTP** — one-time code via WhatsApp (requires WhatsApp business account)

The frontend uses `flowId="sign-in"` — as long as that flow exists and has an active authentication step, the demo will work.

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

> The storage account name must be globally unique, 3–24 lowercase alphanumeric characters.

### 2.3 Create the two containers

```bash
az storage container create \
  --name docs-readonly \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --auth-mode login

az storage container create \
  --name docs-readwrite \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --auth-mode login
```

### 2.4 Seed sample files (optional but recommended for the demo)

```bash
# Upload a couple of PDFs so both roles have something to list
az storage blob upload \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --container-name docs-readonly \
  --name sample-report.pdf \
  --file /path/to/sample-report.pdf \
  --auth-mode login

az storage blob upload \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --container-name docs-readwrite \
  --name upload-test.pdf \
  --file /path/to/upload-test.pdf \
  --auth-mode login
```

### 2.5 Create the Function App

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

> `--assign-identity [system]` enables the System-Assigned Managed Identity in one step.

### 2.6 Note the Managed Identity Object ID

```bash
az functionapp identity show \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --query principalId \
  --output tsv
```

Save this value — you need it for the RBAC assignments below.

### 2.7 Assign RBAC roles to the Managed Identity

**Viewers → `docs-readonly` (Reader)**

```bash
# Get the resource ID of the docs-readonly container
CONTAINER_ID=$(az storage container show \
  --name docs-readonly \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --query id --output tsv)

az role assignment create \
  --assignee <MANAGED_IDENTITY_OBJECT_ID> \
  --role "Storage Blob Data Reader" \
  --scope $CONTAINER_ID
```

**Uploaders → `docs-readwrite` (Contributor)**

```bash
CONTAINER_ID=$(az storage container show \
  --name docs-readwrite \
  --account-name <YOUR_STORAGE_ACCOUNT_NAME> \
  --query id --output tsv)

az role assignment create \
  --assignee <MANAGED_IDENTITY_OBJECT_ID> \
  --role "Storage Blob Data Contributor" \
  --scope $CONTAINER_ID
```

> RBAC propagation takes 1–2 minutes. The Function will return `403` until propagation completes.

### 2.8 Set Function App environment variables

```bash
az functionapp config appsettings set \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --settings \
    DESCOPE_PROJECT_ID=<YOUR_DESCOPE_PROJECT_ID> \
    AZURE_STORAGE_ACCOUNT_NAME=<YOUR_STORAGE_ACCOUNT_NAME>
```

> No storage keys. These two values are the entire configuration surface of the API.

### 2.9 Configure CORS on the Function App

```bash
az functionapp cors add \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group rg-descope-demo \
  --allowed-origins "http://localhost:4000" "http://localhost:5173" "https://<YOUR_FRONTEND_DOMAIN>"
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

Install dependencies:

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
    "AZURE_STORAGE_ACCOUNT_NAME": "<YOUR_STORAGE_ACCOUNT_NAME>"
  },
  "Host": {
    "CORS": "*"
  }
}
```

> For local development the API calls the live Azure storage account using your **Azure CLI credentials** (`az login`). Your CLI user must have `Storage Blob Data Reader` on `docs-readonly` and `Storage Blob Data Contributor` on `docs-readwrite`, or at minimum `Storage Blob Data Owner` on the account.

### 3.4 Run the API locally

The Vite dev server proxies `/api/*` to the live Azure Function, so you don't need to run the API locally unless you want to develop it.

To run locally:

```bash
cd api/DescopeDemo.Function
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"   # macOS with homebrew dotnet@8
func start
```

Expected output:
```
Functions:
    ListDocuments:    [GET]  http://localhost:7071/api/documents
    DownloadDocument: [GET]  http://localhost:7071/api/documents/{name}/download
    UploadDocument:   [POST] http://localhost:7071/api/documents/upload
```

If running locally, update `frontend/vite.config.js` to proxy to `http://localhost:7071` instead of the live Azure URL.

### 3.5 Run the frontend

```bash
cd frontend
npm run dev
```

Open [http://localhost:4000](http://localhost:4000) (or whichever port Vite picks).

---

## Part 4 — Deploy to Azure

### 4.1 Deploy the Function App

```bash
cd api/DescopeDemo.Function
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"   # macOS only
func azure functionapp publish <YOUR_FUNCTION_APP_NAME> --dotnet-isolated
```

Expected output ends with:
```
[Kudu-SyncTriggerStep] completed.
The deployment was successful!
Functions in <YOUR_FUNCTION_APP_NAME>:
    DownloadDocument - [httpTrigger]
    ListDocuments    - [httpTrigger]
    UploadDocument   - [httpTrigger]
```

### 4.2 Verify the live endpoint

```bash
# Unauthenticated request should return 401
curl -i https://<YOUR_FUNCTION_APP_NAME>.azurewebsites.net/api/documents
```

### 4.3 Point the frontend at the live API

In `frontend/vite.config.js`, set the proxy target to your live Function URL:

```js
proxy: {
  '/api': {
    target: 'https://<YOUR_FUNCTION_APP_NAME>.azurewebsites.net',
    changeOrigin: true,
  },
},
```

### 4.4 Deploy the frontend (optional)

The frontend is a static build. Deploy it to [Azure Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/), Vercel, or any static host:

```bash
cd frontend
npm run build
# dist/ contains the built app
```

For a quick demo, running locally (`npm run dev`) against the live Azure Function is sufficient.

---

## Environment Variables Reference

### API (`api/DescopeDemo.Function/local.settings.json` locally, App Settings in Azure)

| Variable | Description |
|---|---|
| `DESCOPE_PROJECT_ID` | Your Descope project ID (e.g. `P2xxxxxxxxxxxxxxxx`) |
| `AZURE_STORAGE_ACCOUNT_NAME` | Storage account name (e.g. `mydemostorage`) |

### Frontend (`frontend/.env.local`)

| Variable | Description |
|---|---|
| `VITE_DESCOPE_PROJECT_ID` | Same Descope project ID — used by the React SDK to load the sign-in flow |

---

## How It Works

### JWT validation (no SDK, no static keys)

```
Descope issues a signed JWT
    └── Azure Function fetches public keys from:
        https://api.descope.com/{PROJECT_ID}/.well-known/openid-configuration
    └── Validates signature, issuer, and expiry using Microsoft.IdentityModel
    └── Keys are cached and auto-refreshed — no secret storage needed
```

### Role extraction

Descope embeds roles as a JSON array claim in the JWT:
```json
{ "roles": ["uploader"] }
```

`JwtSecurityTokenHandler` maps this to .NET's `ClaimTypes.Role`. The Function checks both the raw `"roles"` claim and `ClaimTypes.Role` to handle either mapping.

### Managed Identity (keyless blob access)

`DefaultAzureCredential` chains credential sources automatically:
- **Locally:** Azure CLI credentials (`az login`)
- **In Azure:** System-Assigned Managed Identity

No storage account key, no SAS token, no connection string exists anywhere in this application.

### Container selection

```csharp
var containerName = role == "uploader" ? "docs-readwrite" : "docs-readonly";
```

That's the entirety of the authorization logic. Azure RBAC on the containers is the enforcement layer — the Function could select the wrong container and Azure would still deny the operation.

---

## Project Structure

```
.
├── api/
│   └── DescopeDemo.Function/
│       ├── DescopeJwtValidator.cs   # OIDC JWT validation + role extraction
│       ├── DocumentsFunction.cs     # HTTP endpoints (list, download, upload)
│       ├── Program.cs               # DI setup: BlobServiceClient singleton
│       ├── host.json                # Functions host config + App Insights sampling
│       └── local.settings.json      # Local dev secrets (gitignored)
├── frontend/
│   ├── src/
│   │   ├── App.jsx                  # Auth gate: LoginPage vs Portal
│   │   ├── main.jsx                 # Descope AuthProvider root
│   │   ├── components/
│   │   │   ├── LoginPage.jsx        # Descope sign-in flow
│   │   │   ├── Portal.jsx           # Document list + download
│   │   │   └── UploadPanel.jsx      # File upload (uploader role only)
│   │   └── hooks/
│   │       └── useDocuments.js      # Fetch /api/documents with Bearer token
│   ├── .env.example                 # Copy to .env.local and fill in project ID
│   └── vite.config.js               # Tailwind + /api proxy
└── docs/
    └── plans/                       # Original implementation plan
```

---

## Demo Walkthrough

1. **Open the portal** — sign-in screen backed by Descope
2. **Log in as viewer** — sees document list, no upload panel, header shows `viewer → docs-readonly`
3. **Show Azure Portal** — Managed Identity has Reader on `docs-readonly` only
4. **Attempt upload via DevTools** (proves API enforcement regardless of UI):
   ```js
   fetch('/api/documents/upload', {
     method: 'POST',
     headers: { Authorization: `Bearer ${localStorage.getItem('DS')}`, 'X-Blob-Name': 'test.txt' },
     body: new Blob(['test'])
   }).then(r => r.text()).then(console.log)
   // → "Upload requires the 'uploader' role."
   ```
5. **Sign out → log in as uploader** — upload panel appears
6. **Upload a file** — appears in the list immediately
7. **Show Descope Console** — roles, user assignments, flow editor, no custom authz code
8. **Show Function code** — `DocumentsFunction.cs` is ~130 lines total; zero authz logic beyond role→container mapping

---

## Security Notes

- **Path traversal protection** — `DownloadDocument` rejects blob names containing `/`, `\`, or `..`
- **Filename sanitization** — `UploadDocument` runs `Path.GetFileName()` on the client-supplied `X-Blob-Name` header
- **No overwrite** — uploads fail with 409 if a blob with the same name already exists
- **OIDC key caching** — signing keys are fetched once and refreshed automatically; thread-safe via double-checked locking
- **Infrastructure errors surface as 500** — only `SecurityTokenException` (bad/expired token) returns 401; network failures fetching OIDC config are not silently swallowed

---

## License

MIT

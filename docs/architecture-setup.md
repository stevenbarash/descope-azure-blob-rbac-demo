# Setup — two consoles, no code

All configuration is done in the Descope and Azure consoles. Nothing to deploy or write.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#f8fafc', 'primaryBorderColor': '#e2e8f0', 'primaryTextColor': '#1e293b', 'lineColor': '#64748b', 'clusterBkg': '#f8fafc', 'clusterBorder': '#e2e8f0', 'edgeLabelBackground': '#ffffff'}}}%%
flowchart LR
    subgraph DC ["  Descope Console  "]
        direction TB
        D1["Create a tenant per org\ne.g. org-a, org-b"]
        D2["Add roles to each tenant\nviewer, uploader"]
        D3["Assign each user\na tenant and a role"]
        D1 --> D2 --> D3
    end

    subgraph AZ ["  Azure Portal  "]
        direction TB
        MI["Managed Identity\non the Function App"]
        C1["org-a container"]
        C2["org-b container"]
        MI -->|"Blob Data Contributor"| C1
        MI -->|"Blob Data Contributor"| C2
    end

    DC -->|"tenant ID must equal\ncontainer name"| AZ

    style DC fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style AZ fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style D1 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style D2 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style D3 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style MI fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style C1 fill:#dcfce7,stroke:#16a34a,color:#1e293b
    style C2 fill:#fef9c3,stroke:#ca8a04,color:#1e293b
```

**The only coupling between Descope and Azure** is the tenant ID matching the container name. Once that's in place, the runtime is fully automatic — user role changes in Descope take effect on the next request, with no redeployment.

| What you configure | Where | Effect |
|---|---|---|
| Tenant ID | Descope Console | Determines which Azure container the user accesses |
| Role (viewer / uploader) | Descope Console | Controls whether the user can upload |
| Container name | Azure Portal | Must match the Descope tenant ID |
| Managed Identity RBAC | Azure Portal | Enforces storage-level access control |

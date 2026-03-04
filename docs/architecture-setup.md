# Setup — console only, no code

Tenants and roles are configured in two places. No application code is written for either.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#f8fafc', 'primaryBorderColor': '#cbd5e1', 'primaryTextColor': '#1e293b', 'lineColor': '#64748b', 'clusterBkg': '#f8fafc', 'clusterBorder': '#e2e8f0', 'edgeLabelBackground': '#ffffff'}}}%%
flowchart TB
    subgraph DC["Descope Console"]
        direction TB
        D1["Create tenants\n(one per organization)"]
        D2["Create roles per tenant:\nviewer · uploader"]
        D3["Assign each user\nto a tenant with a role"]
        D1 --> D2 --> D3
    end

    subgraph AZ["Azure Portal"]
        direction TB
        MI["Function App\nSystem-assigned Managed Identity"]
        SA[("Storage Account")]
        CA["&lt;tenant-a&gt;\ncontainer"]
        CB["&lt;tenant-b&gt;\ncontainer"]
        CC["..."]

        SA -.->|contains| CA
        SA -.->|contains| CB
        SA -.->|contains| CC
        MI -->|"Storage Blob Data Contributor"| CA
        MI -->|"Storage Blob Data Contributor"| CB
        MI -->|"Storage Blob Data Contributor"| CC
    end

    DC -->|"tenant ID must match\ncontainer name"| AZ

    style DC fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style AZ fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style D1 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style D2 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style D3 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style MI fill:#bfdbfe,stroke:#1d4ed8,color:#1e293b
    style SA fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style CA fill:#d5e8d4,stroke:#82b366,color:#1e293b
    style CB fill:#fff2cc,stroke:#d6b656,color:#1e293b
    style CC fill:#f8fafc,stroke:#cbd5e1,color:#94a3b8
```

**Tenant** determines which Azure container a user can access — the Descope tenant ID is used directly as the container name.
**Role** determines whether the user can only read (`viewer`) or also upload (`uploader`).

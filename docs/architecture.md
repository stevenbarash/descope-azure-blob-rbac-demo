# Architecture

Multi-tenant document portal: Descope handles auth, Azure RBAC handles storage access, no custom authorization code required.

---

## Overview

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#f8fafc', 'primaryBorderColor': '#cbd5e1', 'primaryTextColor': '#1e293b', 'lineColor': '#94a3b8', 'clusterBkg': '#f8fafc', 'clusterBorder': '#e2e8f0', 'edgeLabelBackground': '#ffffff'}}}%%
flowchart LR
    subgraph FE["React Frontend"]
        UI["Descope sign-in flow\n@descope/react-sdk"]
    end

    subgraph API["Azure Function (.NET 8)"]
        direction TB
        V["Validate JWT\nOIDC discovery"]
        R["Read tenant + role\nfrom tenants claim"]
        C["container = tenant ID\nrole gates upload"]
        V --> R --> C
    end

    subgraph BLOB["Azure Blob Storage"]
        direction TB
        TA["&lt;tenant-a&gt;"]
        TB["&lt;tenant-b&gt;"]
        TC["..."]
    end

    FE -->|"Bearer JWT"| API
    API -->|"Managed Identity\n(no keys)"| BLOB

    style FE fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style API fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style BLOB fill:#d5e8d4,stroke:#82b366,color:#1e293b
    style V fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style R fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style C fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style TA fill:#d5e8d4,stroke:#82b366,color:#1e293b
    style TB fill:#fff2cc,stroke:#d6b656,color:#1e293b
    style TC fill:#f8fafc,stroke:#cbd5e1,color:#94a3b8
```

---

## Setup — UI only, no code

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
        CA["&lt;tenant-a&gt; container"]
        CB["&lt;tenant-b&gt; container"]
        MI -->|"Storage Blob Data Contributor"| CA
        MI -->|"Storage Blob Data Contributor"| CB
    end

    DC -->|"tenant ID = container name"| AZ

    style DC fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style AZ fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style D1 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style D2 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style D3 fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style MI fill:#bfdbfe,stroke:#1d4ed8,color:#1e293b
    style CA fill:#d5e8d4,stroke:#82b366,color:#1e293b
    style CB fill:#fff2cc,stroke:#d6b656,color:#1e293b
```

---

## Runtime — fully automatic

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'actorBkg': '#1e293b', 'actorBorder': '#0f172a', 'actorTextColor': '#f8fafc', 'actorLineColor': '#cbd5e1', 'signalColor': '#64748b', 'signalTextColor': '#334155', 'noteBkgColor': '#f8fafc', 'noteBorderColor': '#e2e8f0', 'noteTextColor': '#64748b', 'activationBkgColor': '#ede9fe', 'activationBorderColor': '#7c3aed', 'sequenceNumberColor': '#7c3aed'}}}%%
sequenceDiagram
    autonumber
    actor User
    participant Descope as Descope
    participant Func as Azure Function
    participant Blob as Blob Storage

    rect rgb(245, 243, 255)
        Note over User,Descope: Sign in
        User->>+Descope: Authenticate (OTP / Magic Link)
        Descope-->>-User: JWT — tenants: { "tenant-a": { "roles": ["viewer"] } }
    end

    rect rgb(239, 246, 255)
        Note over User,Blob: Access documents
        User->>+Func: GET /api/documents (Bearer JWT)
        Note over Func: Validate JWT · read tenant + role · container = tenant ID
        Func->>+Blob: List &lt;tenant-id&gt; container (Managed Identity)
        Blob-->>-Func: File list
        Func-->>-User: { tenantId, role, documents }

        opt uploader role — upload a file
            User->>+Func: POST /api/documents/upload (Bearer JWT)
            Note over Func: Confirms role == uploader (403 if viewer)
            Func->>+Blob: Write to &lt;tenant-id&gt; container
            Blob-->>-Func: 201 Created
            Func-->>-User: Uploaded
        end
    end
```

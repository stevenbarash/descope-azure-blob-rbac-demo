# Architecture

Three moving parts. One key insight: **the JWT carries everything the Function needs — no database, no extra config, no custom auth code.**

---

## How it works at a glance

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#f8fafc', 'primaryBorderColor': '#e2e8f0', 'primaryTextColor': '#1e293b', 'lineColor': '#94a3b8', 'clusterBkg': '#f8fafc', 'clusterBorder': '#e2e8f0', 'edgeLabelBackground': '#ffffff', 'fontSize': '15px'}}}%%
flowchart LR
    U(["User"])

    subgraph DS ["  Descope  "]
        S1["Sign-in flow\nOTP or Magic Link"]
        S2["JWT issued\ntenant ID + role inside"]
        S1 --> S2
    end

    subgraph FN ["  Azure Function  "]
        F1["Validate JWT\nvia OIDC — no secrets"]
        F2["container = tenant ID\nrole gates upload"]
        F1 --> F2
    end

    subgraph BL ["  Azure Blob Storage  "]
        B1["org-a"]
        B2["org-b"]
    end

    U -->|"signs in"| DS
    DS -->|"JWT"| U
    U -->|"JWT on every request"| FN
    FN -->|"Managed Identity\nno storage keys"| BL

    style U fill:#f8fafc,stroke:#cbd5e1,color:#1e293b
    style DS fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style FN fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style BL fill:#f0fdf4,stroke:#16a34a,color:#1e293b
    style S1 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style S2 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style F1 fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style F2 fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style B1 fill:#dcfce7,stroke:#16a34a,color:#1e293b
    style B2 fill:#fef9c3,stroke:#ca8a04,color:#1e293b
```

---

## Setup — two consoles, no code

One-time configuration. Nothing to deploy.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#f8fafc', 'primaryBorderColor': '#e2e8f0', 'primaryTextColor': '#1e293b', 'lineColor': '#64748b', 'clusterBkg': '#f8fafc', 'clusterBorder': '#e2e8f0', 'edgeLabelBackground': '#ffffff'}}}%%
flowchart LR
    subgraph DC ["  Descope Console  "]
        direction TB
        D1["Create a tenant per org"]
        D2["Add roles: viewer, uploader"]
        D3["Assign users to tenant + role"]
        D1 --> D2 --> D3
    end

    subgraph AZ ["  Azure Portal  "]
        direction TB
        MI["Managed Identity\non Function App"]
        C1["org-a container"]
        C2["org-b container"]
        MI -->|"Blob Data Contributor"| C1
        MI -->|"Blob Data Contributor"| C2
    end

    DC -->|"tenant ID must match\ncontainer name"| AZ

    style DC fill:#f5f3ff,stroke:#7c3aed,color:#1e293b
    style AZ fill:#eff6ff,stroke:#1d4ed8,color:#1e293b
    style D1 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style D2 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style D3 fill:#ede9fe,stroke:#7c3aed,color:#1e293b
    style MI fill:#dbeafe,stroke:#1d4ed8,color:#1e293b
    style C1 fill:#dcfce7,stroke:#16a34a,color:#1e293b
    style C2 fill:#fef9c3,stroke:#ca8a04,color:#1e293b
```

> The Descope tenant ID and the Azure container name must match — that's the only coupling between the two systems.

---

## Runtime — sign in, then everything is automatic

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'actorBkg': '#1e293b', 'actorBorder': '#0f172a', 'actorTextColor': '#f8fafc', 'actorLineColor': '#cbd5e1', 'signalColor': '#475569', 'signalTextColor': '#1e293b', 'noteBkgColor': '#f8fafc', 'noteBorderColor': '#e2e8f0', 'noteTextColor': '#475569', 'activationBkgColor': '#dbeafe', 'activationBorderColor': '#1d4ed8'}}}%%
sequenceDiagram
    actor User
    participant D as Descope
    participant F as Azure Function
    participant B as Blob Storage

    Note over User,D: Sign in once
    User->>+D: Authenticate
    D-->>-User: JWT containing tenant ID and role

    Note over User,B: Every request after that
    User->>+F: Request + JWT
    Note over F: Reads tenant ID and role from JWT
    F->>+B: Access [tenant-id] container via Managed Identity
    B-->>-F: Files
    F-->>-User: Done

    Note over F: Upload blocked here if role is viewer
```

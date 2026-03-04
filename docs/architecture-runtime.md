# Runtime Flow

The tenant embedded in the Descope token routes the user to their hotel's container.
The role determines whether they can upload.

## Document access

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
        User->>+Descope: Sign in
        Note over Descope: WhatsApp OTP / Email OTP / Magic Link
        Descope-->>-User: JWT containing tenant (hotel-a or hotel-b) and role (viewer or uploader)
    end

    rect rgb(239, 246, 255)
        Note over User,Blob: Step 2 — Load documents
        User->>+Func: Open document portal
        Note over Func: Verify JWT, read tenant and role from tenants claim
        alt tenant is hotel-a
            Func->>+Blob: Read files from hotel-a container
            Blob-->>-Func: File list
        else tenant is hotel-b
            Func->>+Blob: Read files from hotel-b container
            Blob-->>-Func: File list
        end
        Func-->>-User: File list shown in portal
    end

    rect rgb(240, 253, 244)
        Note over User,Blob: Step 3 — Upload (uploader role only)
        User->>+Func: Select file and upload
        Note over Func: Confirms uploader role, writes to tenant's container
        Func->>+Blob: Write file to hotel-a or hotel-b container
        Blob-->>-Func: File saved
        Func-->>-User: File appears in portal
    end
```

**Tenant isolation:** a Hotel A user never sees Hotel B files — the tenant in their JWT
maps to a separate Azure container and there is no path to the other.

**Role enforcement:** viewers and uploaders in the same hotel see identical documents.
Only uploaders can write. The Managed Identity has Contributor on both containers;
write access is blocked at the application layer for viewers.

## Future — Alfresco SAML SSO

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'actorBkg': '#1e293b', 'actorBorder': '#0f172a', 'actorTextColor': '#f8fafc', 'actorLineColor': '#cbd5e1', 'signalColor': '#64748b', 'signalTextColor': '#334155', 'noteBkgColor': '#f8fafc', 'noteBorderColor': '#e2e8f0', 'noteTextColor': '#64748b', 'activationBkgColor': '#ede9fe', 'activationBorderColor': '#7c3aed'}}}%%
sequenceDiagram
    actor User
    participant Alfresco as Alfresco DMS
    participant Descope as Descope

    User->>Alfresco: Open Alfresco
    Alfresco->>+Descope: Redirect to Descope for login
    Note over Descope: Same sign-in flow as above
    Descope-->>-Alfresco: Confirmation of authenticated user
    Alfresco-->>User: Logged in, no Alfresco password needed
    Note over Alfresco,Descope: One-time setup in both admin consoles. No code.
```

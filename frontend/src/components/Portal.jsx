// ============================================================
// Portal.jsx — Main document portal (shown after sign-in)
//
// How it works:
//   1. useDocuments() fetches GET /api/documents with the user's JWT.
//      The API validates the token and returns { role, container, documents }.
//   2. The role and container name come from the API response — the frontend
//      never assumes or hardcodes what the user can see. The API's answer is
//      the ground truth (it read the role from the verified JWT).
//   3. If the role is "uploader", the upload panel is shown; otherwise it's
//      hidden entirely (not just disabled — the component doesn't render).
//   4. Downloads are fetched via the API (not a direct blob URL) so the
//      Authorization header can be included. Direct blob URLs would require
//      public access or SAS tokens, which we deliberately avoid.
// ============================================================

import { useUser, useDescope, useSession } from '@descope/react-sdk'
import { useDocuments } from '../hooks/useDocuments'
import UploadPanel from './UploadPanel'

// Display helper: formats a byte count as a human-readable string.
function formatSize(bytes) {
  if (!bytes) return '—'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
}

export default function Portal() {
  // user       — display name / email / phone from the Descope session
  // logout     — invalidates the Descope session and clears localStorage
  // sessionToken — the raw JWT, needed to attach as Bearer on download requests
  const { user } = useUser()
  const { logout } = useDescope()
  const { sessionToken } = useSession()

  // data.role      — "viewer" or "uploader" (read from the JWT by the API)
  // data.container — the container the API is reading from ("docs-readonly" or "docs-readwrite")
  // data.documents — array of { name, sizeBytes, lastModified }
  // reload         — re-fetches the document list (called after a successful upload)
  const { data, loading, error, reload } = useDocuments()

  // Derive display values from the API response.
  // We default to '…' while loading rather than showing a stale or wrong value.
  const role = data?.role ?? '…'
  const tenantId = data?.tenantId ?? ''
  const isUploader = role === 'uploader'
  const docs = data?.documents ?? []
  const container = data?.container ?? ''

  // ---- Download handler ----
  //
  // We can't use a plain <a href="..."> for downloads because the blob endpoint
  // requires an Authorization header, and browsers don't send custom headers for
  // anchor tag navigation. Instead we:
  //   1. Fetch the file via the API with the Bearer token.
  //   2. Convert the response to a Blob object in memory.
  //   3. Create a temporary object URL from the Blob.
  //   4. Simulate a click on a hidden <a> element to trigger the browser's
  //      native "Save As" / download behaviour.
  //   5. Immediately revoke the object URL to free memory.
  async function handleDownload(blobName) {
    const res = await fetch(`/api/documents/${encodeURIComponent(blobName)}/download`, {
      headers: { Authorization: `Bearer ${sessionToken}` },
    })
    if (!res.ok) {
      alert(`Download failed: ${res.status}`)
      return
    }

    // Buffer the response into a Blob, then create a temporary browser-local URL.
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)

    // Trigger the download by programmatically clicking a temporary <a> element.
    const a = document.createElement('a')
    a.href = url
    a.download = blobName
    a.click()

    // Clean up the object URL immediately — the browser has queued the download.
    URL.revokeObjectURL(url)
  }

  return (
    <div className="min-h-screen bg-slate-50">

      {/* ---- Header ---- */}
      {/*
        Shows the authenticated user's identity and their role from the API.
        The "→ container" suffix makes the RBAC routing visible during the demo:
        viewer sees "viewer → docs-readonly"
        uploader sees "uploader → docs-readwrite"
      */}
      <header className="bg-white border-b border-slate-200 px-6 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-800">Document Portal</h1>
          <p className="text-sm text-slate-500">
            {user?.email ?? user?.phone ?? 'User'} ·{' '}
            {tenantId && (
              <span className="font-medium text-slate-700">{tenantId}</span>
            )}
            {tenantId && ' · '}
            <span className={`font-medium ${isUploader ? 'text-green-600' : 'text-blue-600'}`}>
              {role}
            </span>
            {container && (
              <span className="text-slate-400"> → {container}</span>
            )}
          </p>
        </div>

        {/*
          logout() is wrapped in an arrow function to prevent React from passing
          the SyntheticEvent click object as the first argument to logout(), which
          would cause the Descope SDK to interpret it as a session token string.
        */}
        <button
          onClick={() => logout()}
          className="text-sm text-slate-500 hover:text-slate-800 underline"
        >
          Sign out
        </button>
      </header>

      <main className="max-w-4xl mx-auto px-6 py-8 space-y-6">

        {/*
          Upload panel is only rendered for uploaders — it's not hidden/disabled,
          it simply doesn't exist in the DOM for viewer-role users.
          onUploadComplete triggers a reload of the document list so the newly
          uploaded file appears immediately without a full page refresh.
        */}
        {isUploader && <UploadPanel onUploadComplete={reload} />}

        {/* ---- Document list ---- */}
        <section>
          <h2 className="text-lg font-semibold text-slate-700 mb-4">Documents</h2>

          {loading && <p className="text-slate-400 text-sm">Loading documents…</p>}
          {error && <p className="text-red-500 text-sm">Error: {error}</p>}
          {!loading && !error && docs.length === 0 && (
            <p className="text-slate-400 text-sm">No documents in this container.</p>
          )}

          <ul className="space-y-2">
            {docs.map((doc) => (
              <li
                key={doc.name}
                className="bg-white rounded-xl border border-slate-200 px-5 py-4 flex items-center justify-between"
              >
                <div>
                  <p className="font-medium text-slate-800 text-sm">{doc.name}</p>
                  <p className="text-xs text-slate-400">
                    {formatSize(doc.sizeBytes)}
                    {doc.lastModified && ` · ${new Date(doc.lastModified).toLocaleDateString()}`}
                  </p>
                </div>

                {/* See handleDownload above for why this is a button rather than an <a> tag. */}
                <button
                  onClick={() => handleDownload(doc.name)}
                  className="text-sm font-medium text-blue-600 hover:text-blue-800"
                >
                  Download
                </button>
              </li>
            ))}
          </ul>
        </section>
      </main>
    </div>
  )
}

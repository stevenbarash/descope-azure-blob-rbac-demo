// ============================================================
// UploadPanel.jsx — File upload UI (rendered for uploaders only)
//
// How it works:
//   1. User picks a file with the file input.
//   2. On click, we POST the raw file body to /api/documents/upload.
//   3. The Authorization header carries the Descope JWT so the API can:
//        a. Validate the token.
//        b. Confirm the user has the "uploader" role.
//        c. Write the file to the docs-readwrite container via Managed Identity.
//   4. The filename is sent in the X-Blob-Name header. The API sanitizes it
//      server-side with Path.GetFileName() before storing.
//   5. On success, onUploadComplete() is called to refresh the document list
//      in the parent <Portal /> component.
//
// This component is only rendered when role === "uploader" (see Portal.jsx).
// The API enforces the role check independently — the hidden UI is not the
// only gate.
// ============================================================

import { useSession } from '@descope/react-sdk'
import { useRef, useState } from 'react'

export default function UploadPanel({ onUploadComplete }) {
  // sessionToken is the Descope JWT, used as the Bearer token on the upload request.
  const { sessionToken } = useSession()
  const fileRef = useRef()
  const [uploading, setUploading] = useState(false)
  const [status, setStatus] = useState(null) // { ok: bool, message: string } | null

  async function handleUpload() {
    const file = fileRef.current?.files[0]
    if (!file) return

    setUploading(true)
    setStatus(null)

    try {
      // Send the file as the raw request body (not multipart/form-data).
      // The API reads req.Body directly and streams it straight to Azure Blob Storage,
      // avoiding buffering the entire file in memory on the Function host.
      //
      // X-Blob-Name: the desired filename in the blob container.
      //   The API runs Path.GetFileName() on this before storage, so path traversal
      //   attempts like "../../evil.sh" are stripped to just "evil.sh".
      //
      // Content-Type: passed through so Azure stores the correct MIME type
      //   alongside the blob (used for Content-Type on downloads).
      const res = await fetch('/api/documents/upload', {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${sessionToken}`,
          'Content-Type': file.type || 'application/octet-stream',
          'X-Blob-Name': file.name,
        },
        body: file,
      })

      // Surface role enforcement errors specifically so the demo can show
      // that the API, not just the UI, is enforcing access control.
      if (res.status === 403) throw new Error('Upload not permitted for your role.')
      if (!res.ok) throw new Error(`Upload failed: ${res.status}`)

      setStatus({ ok: true, message: `"${file.name}" uploaded successfully.` })
      fileRef.current.value = '' // Clear the file input ready for the next upload.

      // Tell the parent to refresh the document list so the new file appears immediately.
      onUploadComplete()
    } catch (e) {
      setStatus({ ok: false, message: e.message })
    } finally {
      setUploading(false)
    }
  }

  return (
    <section className="bg-white rounded-xl border border-slate-200 px-5 py-5">
      <h2 className="text-lg font-semibold text-slate-700 mb-4">Upload Document</h2>
      <div className="flex items-center gap-4 flex-wrap">
        <input
          ref={fileRef}
          type="file"
          className="text-sm text-slate-600 file:mr-4 file:py-2 file:px-4 file:rounded-lg file:border-0 file:text-sm file:font-medium file:bg-blue-50 file:text-blue-700 hover:file:bg-blue-100"
        />
        <button
          onClick={handleUpload}
          disabled={uploading}
          className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          {uploading ? 'Uploading…' : 'Upload'}
        </button>
      </div>

      {/* Show success (green) or error (red) feedback after each upload attempt. */}
      {status && (
        <p className={`mt-3 text-sm ${status.ok ? 'text-green-600' : 'text-red-600'}`}>
          {status.message}
        </p>
      )}
    </section>
  )
}

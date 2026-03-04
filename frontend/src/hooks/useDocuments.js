// ============================================================
// useDocuments.js — Fetches the document list from the API
//
// How it works:
//   1. Read the Descope session token from AuthProvider context.
//      (The token is the signed JWT Descope issued at sign-in.)
//   2. Call GET /api/documents with "Authorization: Bearer <token>".
//      (Vite proxies /api/* to the Azure Function in development.)
//   3. The Azure Function validates the token, reads the role, and
//      returns { role, container, documents: [...] }.
//   4. Expose the result plus loading/error state to the caller.
//
// The hook re-runs automatically when sessionToken changes (i.e., on
// sign-in or after a token refresh). Callers can also trigger a manual
// reload via the returned `reload` function (used after an upload).
// ============================================================

import { useSession } from '@descope/react-sdk'
import { useState, useEffect } from 'react'

/**
 * Fetches the document list from the API and exposes reload/loading/error state.
 *
 * `sessionToken` is the raw Descope JWT string. The SDK stores it in localStorage
 * and automatically refreshes it before expiry via the AuthProvider. We send it
 * as a Bearer token so the Azure Function can validate it server-side.
 */
export function useDocuments() {
  // sessionToken: the raw JWT string managed by the Descope AuthProvider.
  // It is undefined until the user is authenticated.
  const { sessionToken } = useSession()
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      // Send the JWT as a Bearer token. The Azure Function validates this token
      // server-side using Descope's OIDC public keys — the API never trusts the
      // role or user identity from the frontend directly.
      const res = await fetch('/api/documents', {
        headers: { Authorization: `Bearer ${sessionToken}` },
      })
      if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)

      // The response shape is: { role, container, documents: [{ name, sizeBytes, lastModified }] }
      // On success, replace stale data so the UI always reflects the latest list.
      setData(await res.json())
    } catch (e) {
      // Clear stale data on error so the UI doesn't show an old document list
      // alongside an error message (e.g., after a token expiry mid-session).
      setData(null)
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  // Re-fetch whenever the session token changes.
  // sessionToken is undefined before sign-in and populated after, so this
  // naturally triggers the first load as soon as the user authenticates.
  useEffect(() => {
    if (sessionToken) load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionToken])

  return { data, loading, error, reload: load }
}

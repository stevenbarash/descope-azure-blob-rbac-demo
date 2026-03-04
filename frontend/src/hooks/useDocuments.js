// ============================================================
// useDocuments.js — Fetches the document list from the API
//
// How it works:
//   1. Read the Descope session token from AuthProvider context.
//      (The token is the signed JWT Descope issued at sign-in.)
//   2. Call GET /api/documents with "Authorization: Bearer <token>".
//      (Vite proxies /api/* to the Azure Function in development.)
//   3. The Azure Function validates the token, reads the role, and
//      returns { tenantId, role, container, documents: [...] }.
//   4. Expose the result plus loading/error state to the caller.
//
// Token handling:
//   The Descope SDK refreshes the session token automatically before it
//   expires. We keep a ref to always use the latest token, and we
//   re-fetch whenever the tab becomes visible again — so returning after
//   an idle period (during which the SDK may have silently refreshed)
//   always loads fresh data with a valid token.
// ============================================================

import { useSession } from '@descope/react-sdk'
import { useState, useEffect, useRef } from 'react'

/**
 * Fetches the document list from the API and exposes reload/loading/error state.
 *
 * Uses a ref for the session token so `load` always reads the current value
 * even when called from a long-lived event listener (e.g., visibilitychange).
 */
export function useDocuments() {
  const { sessionToken } = useSession()
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Keep a ref in sync with sessionToken so event handlers always read the
  // latest value without needing to be re-registered on every token change.
  const tokenRef = useRef(sessionToken)
  useEffect(() => {
    tokenRef.current = sessionToken
  }, [sessionToken])

  async function load() {
    const token = tokenRef.current
    if (!token) return
    setLoading(true)
    setError(null)
    try {
      const res = await fetch('/api/documents', {
        headers: { Authorization: `Bearer ${token}` },
      })
      if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
      setData(await res.json())
    } catch (e) {
      setData(null)
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  // Re-fetch when the session token changes (sign-in or SDK-driven refresh).
  useEffect(() => {
    if (sessionToken) load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionToken])

  // Re-fetch when the user returns to the tab.
  // The SDK may have silently refreshed the token while the tab was hidden;
  // this ensures the document list is always fresh on return.
  useEffect(() => {
    function handleVisibility() {
      if (document.visibilityState === 'visible') load()
    }
    document.addEventListener('visibilitychange', handleVisibility)
    return () => document.removeEventListener('visibilitychange', handleVisibility)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return { data, loading, error, reload: load }
}

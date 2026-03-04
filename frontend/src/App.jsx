// ============================================================
// App.jsx — Authentication gate
//
// How it works:
//   This component is the single decision point between the login screen
//   and the document portal. It reads session state from Descope's
//   AuthProvider (set up in main.jsx) and renders accordingly:
//
//   isSessionLoading = true  → show a spinner
//     (AuthProvider is checking localStorage for an existing session)
//
//   isAuthenticated = true   → render <Portal />
//     (valid, non-expired Descope JWT exists in localStorage)
//
//   isAuthenticated = false  → render <LoginPage />
//     (no session, or session expired)
//
//   When the user completes sign-in via the Descope flow in <LoginPage />,
//   the AuthProvider detects the new session and flips isAuthenticated to
//   true, causing this component to re-render and swap to <Portal />.
//   No manual navigation or state management needed.
// ============================================================

import { useSession } from '@descope/react-sdk'
import LoginPage from './components/LoginPage'
import Portal from './components/Portal'

export default function App() {
  // isSessionLoading is true during the brief moment on page load when
  // AuthProvider is reading the session from localStorage. We wait for
  // it to finish before rendering either screen to avoid a flash of the
  // login page for already-authenticated users.
  const { isAuthenticated, isSessionLoading } = useSession()

  if (isSessionLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-50">
        <p className="text-slate-400 text-sm">Loading…</p>
      </div>
    )
  }

  // The auth gate: authenticated users see the portal, everyone else sees login.
  return isAuthenticated ? <Portal /> : <LoginPage />
}

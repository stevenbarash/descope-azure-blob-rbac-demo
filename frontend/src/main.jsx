// ============================================================
// main.jsx — Application entry point
//
// How it works:
//   AuthProvider wraps the entire app and manages the Descope session.
//   It reads the session from localStorage on load, automatically refreshes
//   the JWT before it expires, and exposes session state to all child
//   components via React context.
//
//   Any component in the tree can then call:
//     useSession()  → { isAuthenticated, isSessionLoading, sessionToken }
//     useUser()     → { user }   (email, phone, name, etc.)
//     useDescope()  → { logout } (and other SDK methods)
//
//   The projectId here connects the SDK to your specific Descope project,
//   which determines which sign-in flow, branding, and user directory to use.
// ============================================================

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider } from '@descope/react-sdk'
import App from './App.jsx'
import './index.css'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    {/*
      AuthProvider must wrap the entire app so that useSession / useUser /
      useDescope hooks work in any child component. The projectId is read
      from the .env.local file (VITE_DESCOPE_PROJECT_ID) and baked into
      the build at compile time by Vite.
    */}
    <AuthProvider projectId={import.meta.env.VITE_DESCOPE_PROJECT_ID}>
      <App />
    </AuthProvider>
  </StrictMode>
)

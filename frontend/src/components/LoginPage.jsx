// ============================================================
// LoginPage.jsx — Descope-powered sign-in screen
//
// How it works:
//   The <Descope> component renders a fully managed sign-in flow hosted
//   by Descope. The flow is configured in the Descope Console (Flows →
//   sign-in) and can include any combination of:
//     - Email OTP (one-time code sent to email)
//     - Magic Link (passwordless link sent to email)
//     - WhatsApp OTP (one-time code via WhatsApp)
//
//   When the user completes the flow successfully, Descope:
//     1. Issues a signed JWT containing the user's ID, email, and roles.
//     2. Stores the JWT in localStorage.
//     3. Updates the AuthProvider's session state.
//
//   App.jsx watches that session state and automatically swaps this page
//   for <Portal /> — no routing or manual redirect needed.
// ============================================================

import { Descope } from '@descope/react-sdk'

export default function LoginPage() {
  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-slate-50 gap-8 px-4">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-slate-800">Document Portal</h1>
        <p className="mt-2 text-slate-500 text-sm">Sign in to access your documents</p>
      </div>

      <div className="bg-white rounded-2xl shadow-lg p-8 w-full max-w-md">
        {/*
          flowId="sign-in" references the flow configured in the Descope Console.
          The entire sign-in UI (branding, steps, error messages) is rendered and
          managed by Descope — no auth UI code lives here.

          onSuccess is intentionally empty: the Descope SDK updates the AuthProvider
          context automatically when the flow completes. App.jsx detects the new
          session and renders <Portal /> without any manual navigation.

          onError logs auth failures (wrong code, expired link, etc.) to the console
          for debugging during development.
        */}
        <Descope
          flowId="sign-in-otp"
          onSuccess={() => {}}
          onError={(e) => console.error('Auth error:', e)}
          theme="light"
        />
      </div>

      <p className="text-xs text-slate-400">Powered by Descope</p>
    </div>
  )
}

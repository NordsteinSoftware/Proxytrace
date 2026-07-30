import { useEffect } from 'react';
import { BrowserRouter } from 'react-router';
import { AuthProvider } from 'react-oidc-context';
import { Trans } from '@lingui/react/macro';
import { PageLoader } from './PageLoader';
import { AppRoutes } from './AppRoutes';
import { OidcAuthGate, LocalAuthGate } from './AuthGates';
import { dynamicActivate, DEFAULT_LOCALE } from '../i18n';
import { oidcConfig } from '../auth/oidcConfig';
import { LocalAuthProvider } from '../auth/local/LocalAuthProvider';
import { CurrentUserContext } from '../auth/useCurrentUser';
import { KioskContext } from '../contexts/KioskContext';
import { setApiReadOnly } from '../api/client';
import { useAppConfig } from '../hooks/useAppConfig';
import { useAuthMode } from '../auth/authMode';
import { cn } from '../lib/cn';

function KioskShell({ interactive }: { interactive: boolean }) {
  useEffect(() => {
    // A kiosk has no account and no language picker (LocaleSync + the picker are both off here), so
    // pin the UI to the source language (English) regardless of the machine's browser/cache locale —
    // a public demo must read consistently. Not cached: this is implicit to kiosk, not a preference.
    void dynamicActivate(DEFAULT_LOCALE);
  }, []);
  useEffect(() => {
    // Two halves of the same switch, and they must stay together: `setApiReadOnly` is the
    // enforcement (every mutating request is refused at the transport — see `api/client.ts`), the
    // `kiosk` body class is the affordance that dims write controls (index.css). Only for a
    // read-only kiosk; an interactive one is a normal single-user instance.
    if (interactive) return;
    setApiReadOnly(true);
    const kioskClass = cn('kiosk');
    document.body.classList.add(kioskClass);
    return () => {
      setApiReadOnly(false);
      document.body.classList.remove(kioskClass);
    };
  }, [interactive]);
  return (
    <KioskContext.Provider value={{ enabled: true, interactive }}>
      <BrowserRouter>
        {/*
          The role is as load-bearing as the email: every `adminOnly` surface — Settings, the
          project selector's admin actions, cost budgets — gates on `useCurrentUser()?.role`, so
          omitting it left them all hidden in kiosk regardless of what the demo user was actually
          seeded as. That silently defeated seeding the demo user as an Admin: the API answered
          "Admin" while the UI behaved as if it were a Member, which reads as a missing feature
          rather than a demo boundary. Must stay in step with `CoreSeedScenario` (pinned by
          `DemoSeedingTests.Seed_DemoUser_Is_An_Admin`). What a visitor may actually change is
          bounded by `interactive` above, not by this role.
        */}
        <CurrentUserContext.Provider
          value={{ email: 'demo@proxytrace.dev', role: 'Admin', signOut: () => {} }}
        >
          <AppRoutes />
        </CurrentUserContext.Provider>
      </BrowserRouter>
    </KioskContext.Provider>
  );
}

/** Picks the auth strategy from app config / auth mode and mounts the router with the right gate:
 *  a login-free kiosk, local cookie auth, or OIDC. */
export function ModeShell() {
  const { data: appConfig } = useAppConfig();
  const { data, isLoading, error } = useAuthMode();
  if (appConfig?.kiosk) return <KioskShell interactive={!!appConfig.interactive} />;
  if (isLoading) return <PageLoader />;
  if (error || !data) {
    return (
      <div className="flex min-h-screen items-center justify-center text-body text-danger">
        <Trans>Could not detect auth mode.</Trans>
      </div>
    );
  }
  if (data.mode === 'local') {
    return (
      <BrowserRouter>
        <LocalAuthProvider>
          <LocalAuthGate>
            <AppRoutes />
          </LocalAuthGate>
        </LocalAuthProvider>
      </BrowserRouter>
    );
  }
  return (
    <AuthProvider {...oidcConfig}>
      <BrowserRouter>
        <OidcAuthGate>
          <AppRoutes />
        </OidcAuthGate>
      </BrowserRouter>
    </AuthProvider>
  );
}

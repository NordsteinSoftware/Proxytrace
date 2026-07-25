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
    // The `kiosk` body class drives the read-only [data-write] kill-switch (index.css). Only
    // apply it for a read-only kiosk; an interactive kiosk leaves write controls live.
    if (interactive) return;
    const kioskClass = cn('kiosk');
    document.body.classList.add(kioskClass);
    return () => document.body.classList.remove(kioskClass);
  }, [interactive]);
  return (
    <KioskContext.Provider value={{ enabled: true, interactive }}>
      <BrowserRouter>
        <CurrentUserContext.Provider value={{ email: 'demo@proxytrace.dev', signOut: () => {} }}>
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

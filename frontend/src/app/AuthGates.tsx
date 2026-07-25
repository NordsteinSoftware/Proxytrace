import { lazy, Suspense, useEffect } from 'react';
import { Navigate, useLocation } from 'react-router';
import { useAuth } from 'react-oidc-context';
import { Trans } from '@lingui/react/macro';
import { PageLoader } from './PageLoader';
import { setAccessToken, setUnauthorizedHandler } from '../auth/token';
import { CurrentUserContext, type CurrentUser } from '../auth/useCurrentUser';
import useLocalAuth from '../hooks/useLocalAuth';

const Login = lazy(() => import('../features/auth/Login'));

export function OidcAuthGate({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  useEffect(() => {
    setAccessToken(auth.user?.access_token ?? null);
  }, [auth.user?.access_token]);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      void auth.signinRedirect();
    });
    return () => setUnauthorizedHandler(null);
  }, [auth]);

  if (auth.isLoading || auth.activeNavigator) return <PageLoader />;
  if (auth.error) {
    return (
      <div className="flex min-h-screen items-center justify-center text-body text-danger">
        <Trans>Auth error: {auth.error.message}</Trans>
      </div>
    );
  }
  if (!auth.isAuthenticated) {
    return (
      <Suspense fallback={<PageLoader />}>
        <Login />
      </Suspense>
    );
  }
  const profile = auth.user?.profile as { email?: string } | undefined;
  const currentUser: CurrentUser | null = profile?.email
    ? { email: profile.email, signOut: () => void auth.signoutRedirect() }
    : null;
  return <CurrentUserContext.Provider value={currentUser}>{children}</CurrentUserContext.Provider>;
}

export function LocalAuthGate({ children }: { children: React.ReactNode }) {
  const local = useLocalAuth();
  const { pathname } = useLocation();

  // The session lives in an httpOnly cookie; wait for the one-time /me restore so an
  // authenticated reload doesn't flash the login form.
  if (local.isRestoring) return <PageLoader />;

  const currentUser: CurrentUser | null = local.user
    ? { email: local.user.email, role: local.user.role, signOut: () => local.signoutRedirect() }
    : null;

  // Honest /login URL: signout lands here, and an authenticated visit bounces home.
  // Unauthenticated users on any *other* path still get the login form rendered in
  // place (below), so deep links survive the login round-trip.
  if (pathname === '/login') {
    return local.isAuthenticated ? (
      <Navigate to="/dashboard" replace />
    ) : (
      <Suspense fallback={<PageLoader />}>
        <Login />
      </Suspense>
    );
  }

  if (
    pathname === '/signup' ||
    pathname === '/setup' ||
    pathname === '/forgot-password' ||
    pathname === '/reset-password'
  ) {
    return <CurrentUserContext.Provider value={currentUser}>{children}</CurrentUserContext.Provider>;
  }
  if (!local.isAuthenticated) {
    return (
      <Suspense fallback={<PageLoader />}>
        <Login />
      </Suspense>
    );
  }
  return <CurrentUserContext.Provider value={currentUser}>{children}</CurrentUserContext.Provider>;
}

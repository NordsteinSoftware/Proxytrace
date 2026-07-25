import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { setAccessToken, setUnauthorizedHandler } from "../token";
import { localAuthApi } from "./localAuthApi";
import LocalAuthContext, {
  decode,
  type LocalAuthContextValue,
  type LocalUser,
} from "./LocalAuthContext";

/**
 * Local-mode session handling. The durable session is an httpOnly cookie set by the
 * backend on login/signup/setup — never persisted to script-readable storage. The JWT
 * from the login response is decoded for immediate UI state and kept in memory only;
 * after a reload the cookie restores the session via `/api/auth/me`.
 */
export function LocalAuthProvider({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [user, setUser] = useState<LocalUser | null>(null);
  // True until the cookie session has been checked once — gates render so an
  // authenticated reload doesn't flash the login form.
  const [isRestoring, setIsRestoring] = useState(true);

  useEffect(() => {
    let cancelled = false;
    localAuthApi
      .me()
      .then((me) => {
        if (!cancelled) setUser(me);
      })
      .catch(() => {
        // 401 just means "not signed in"; network errors fall through to the login form.
      })
      .finally(() => {
        if (!cancelled) setIsRestoring(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const setToken = useCallback(
    (t: string | null) => {
      setAccessToken(t);
      setUser(t ? decode(t) : null);
      queryClient.clear();
    },
    [queryClient],
  );

  useEffect(() => {
    setUnauthorizedHandler(() => {
      // Session expired/invalid: drop the cookie server-side too, then show login.
      void localAuthApi.logout().catch(() => {});
      setToken(null);
      navigate("/login");
    });
    return () => setUnauthorizedHandler(null);
  }, [navigate, setToken]);

  const value = useMemo<LocalAuthContextValue>(
    () => ({
      isAuthenticated: !!user,
      isRestoring,
      user,
      setToken,
      signinRedirect: () => navigate("/login"),
      signoutRedirect: () => {
        void localAuthApi.logout().catch(() => {});
        setToken(null);
        navigate("/login");
      },
    }),
    [user, isRestoring, setToken, navigate],
  );

  return (
    <LocalAuthContext.Provider value={value}>
      {children}
    </LocalAuthContext.Provider>
  );
}

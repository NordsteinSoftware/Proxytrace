import { useEffect } from 'react';
import { useNavigate } from 'react-router';
import { setErrorLogNavigator } from '../lib/errorLogNav';

// Registers a deep-link navigator (used by error toasts to jump to the captured error in the
// Error Log) only while an admin is viewing — the Error Log is admin-only. Lives inside the
// Router so it can use `useNavigate`; the ToastProvider above the Router reads it via the bridge.
export function ErrorLogNavBridge({ enabled }: { enabled: boolean }) {
  const navigate = useNavigate();
  useEffect(() => {
    if (!enabled) {
      setErrorLogNavigator(null);
      return;
    }
    setErrorLogNavigator(id => navigate(`/settings/error-log?error=${encodeURIComponent(id)}`));
    return () => setErrorLogNavigator(null);
  }, [enabled, navigate]);
  return null;
}

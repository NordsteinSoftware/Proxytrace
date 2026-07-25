import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Link, useNavigate } from 'react-router';
import { localAuthApi } from '../../../auth/local/localAuthApi';
import { Button } from '../../../components/ui/Button';
import { Input } from '../../../components/ui/Input';
import useLocalAuth from '../../../hooks/useLocalAuth';
import { AuthCard } from './AuthCard';
import { MfaChallengeForm } from './MfaChallengeForm';

/** Email + password sign-in for local-auth installs, with an optional TOTP second-factor step. */
export function LocalLogin() {
  const { t } = useLingui();
  const { setToken } = useLocalAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  // Set when the password step passes but the account requires a TOTP second factor.
  const [challengeToken, setChallengeToken] = useState<string | null>(null);

  const completeLogin = (token: string) => {
    setToken(token);
    navigate('/');
  };

  return (
    <AuthCard>
      {challengeToken ? (
          <MfaChallengeForm challengeToken={challengeToken} onVerified={completeLogin} />
        ) : (
          <form
            data-testid="login-form"
            className="space-y-3"
            onSubmit={async (e) => {
              e.preventDefault();
              setErr(null);
              setSubmitting(true);
              try {
                const outcome = await localAuthApi.login(email, password);
                if (outcome.mfaRequired) {
                  setChallengeToken(outcome.challengeToken);
                } else {
                  completeLogin(outcome.token);
                }
              } catch {
                setErr(t`Invalid email or password.`);
              } finally {
                setSubmitting(false);
              }
            }}
          >
            <h1 className="text-h1 font-semibold"><Trans>Sign in</Trans></h1>
            <Input
              data-testid="login-email"
              placeholder={t`Email`}
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
            <Input
              data-testid="login-password"
              placeholder={t`Password`}
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            {err && <p data-testid="login-error" className="text-body text-danger">{err}</p>}
            <Button data-testid="login-submit" type="submit" fullWidth loading={submitting}>
              <Trans>Sign in</Trans>
            </Button>
            <Link
              to="/forgot-password"
              data-testid="login-forgot-password"
              className="block text-center text-body-sm text-accent hover:underline"
            >
              <Trans>Forgot password?</Trans>
            </Link>
          </form>
        )}
    </AuthCard>
  );
}

import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { localAuthApi } from '../../auth/local/localAuthApi';
import { passwordIsValid } from '../../auth/password';
import { PasswordRequirements } from '../../components/auth/PasswordRequirements';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import useLocalAuth from '../../hooks/useLocalAuth';
import { AuthCard } from './components/AuthCard';
import { MfaChallengeForm } from './components/MfaChallengeForm';

export default function ResetPassword() {
  const { t } = useLingui();
  const { setToken } = useLocalAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- query-param name, not UI copy
  const token = params.get('token') ?? '';
  const [password, setPassword] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  // Set when the account has MFA: the reset succeeded but a second factor is still required.
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const valid = passwordIsValid(password);

  const completeLogin = (issued: string) => {
    setToken(issued);
    navigate('/');
  };

  if (!token) {
    return (
      <AuthCard>
        <InvalidLink />
      </AuthCard>
    );
  }

  if (challengeToken) {
    return (
      <AuthCard>
        <MfaChallengeForm challengeToken={challengeToken} onVerified={completeLogin} />
      </AuthCard>
    );
  }

  return (
    <AuthCard>
      <form
        data-testid="reset-password-form"
        className="space-y-3"
        onSubmit={async (e) => {
          e.preventDefault();
          if (!valid) return;
          setErr(null);
          setSubmitting(true);
          try {
            const outcome = await localAuthApi.resetPassword(token, password);
            if (outcome.mfaRequired) {
              setChallengeToken(outcome.challengeToken);
            } else {
              completeLogin(outcome.token);
            }
          } catch (caught) {
            const status = (caught as { status?: number }).status;
            setErr(
              status === 410
                ? t`This reset link is invalid or has expired. Request a new one.`
                : t`Could not reset your password. Please try again.`,
            );
          } finally {
            setSubmitting(false);
          }
        }}
      >
        <h1 className="text-h1 font-semibold"><Trans>Choose a new password</Trans></h1>
        <Input
          data-testid="reset-password-input"
          placeholder={t`New password`}
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <PasswordRequirements password={password} />
        {err && <p data-testid="reset-password-error" className="text-body text-danger">{err}</p>}
        <Button data-testid="reset-password-submit" type="submit" fullWidth loading={submitting} disabled={!valid}>
          <Trans>Set new password</Trans>
        </Button>
        <Link to="/login" className="block text-center text-body-sm text-accent hover:underline">
          <Trans>Back to sign in</Trans>
        </Link>
      </form>
    </AuthCard>
  );
}

function InvalidLink() {
  return (
    <div data-testid="reset-password-invalid" className="space-y-3 text-center">
      <h1 className="text-h1 font-semibold"><Trans>Invalid reset link</Trans></h1>
      <p className="text-body-sm text-muted">
        <Trans>This password reset link is missing or malformed. Request a new one.</Trans>
      </p>
      <Link to="/forgot-password" className="block text-body-sm text-accent hover:underline">
        <Trans>Request a new link</Trans>
      </Link>
    </div>
  );
}

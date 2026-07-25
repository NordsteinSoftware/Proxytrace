import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Link } from 'react-router';
import { localAuthApi } from '../../auth/local/localAuthApi';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { AuthCard } from './components/AuthCard';

export default function ForgotPassword() {
  const { t } = useLingui();
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [sent, setSent] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  if (sent) {
    return (
      <AuthCard>
        <div data-testid="forgot-password-confirmation" className="space-y-3 text-center">
          <h1 className="text-h1 font-semibold"><Trans>Check your inbox</Trans></h1>
          <p className="text-body-sm text-muted">
            <Trans>
              If an account exists for that email, password reset instructions are on the way. If
              email delivery is not configured on this server, contact your administrator to obtain a
              reset link.
            </Trans>
          </p>
          <Link to="/login" className="block text-body-sm text-accent hover:underline">
            <Trans>Back to sign in</Trans>
          </Link>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard>
      <form
        data-testid="forgot-password-form"
        className="space-y-3"
        onSubmit={async (e) => {
          e.preventDefault();
          setErr(null);
          setSubmitting(true);
          try {
            await localAuthApi.forgotPassword(email);
            setSent(true);
          } catch (caught) {
            const status = (caught as { status?: number }).status;
            setErr(
              status === 429
                ? t`Too many attempts. Please wait a few minutes and try again.`
                : t`Something went wrong. Please try again.`,
            );
          } finally {
            setSubmitting(false);
          }
        }}
      >
        <h1 className="text-h1 font-semibold"><Trans>Reset your password</Trans></h1>
        <p className="text-body-sm text-muted">
          <Trans>Enter your email and we'll send a link to reset your password.</Trans>
        </p>
        <Input
          data-testid="forgot-password-email"
          placeholder={t`Email`}
          type="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        {err && <p data-testid="forgot-password-error" className="text-body text-danger">{err}</p>}
        <Button data-testid="forgot-password-submit" type="submit" fullWidth loading={submitting}>
          <Trans>Send reset link</Trans>
        </Button>
        <Link to="/login" className="block text-center text-body-sm text-accent hover:underline">
          <Trans>Back to sign in</Trans>
        </Link>
      </form>
    </AuthCard>
  );
}

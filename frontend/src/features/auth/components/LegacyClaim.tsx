import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router';
import { localAuthApi } from '../../../auth/local/localAuthApi';
import { passwordIsValid } from '../../../auth/password';
import { PasswordRequirements } from '../../../components/auth/PasswordRequirements';
import { Button } from '../../../components/ui/Button';
import { Input } from '../../../components/ui/Input';
import useLocalAuth from '../../../hooks/useLocalAuth';
import { AuthCard } from './AuthCard';

/** One-time migration screen: an existing user sets a password after local auth is enabled. */
export function LegacyClaim() {
  const { t } = useLingui();
  const { setToken } = useLocalAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const valid = passwordIsValid(password);

  return (
    <AuthCard size="lg">
      <form
        className="space-y-3"
        onSubmit={async (e) => {
          e.preventDefault();
          if (!valid) return;
          setErr(null);
          setSubmitting(true);
          try {
            const r = await localAuthApi.claimLegacy(email, password);
            setToken(r.token);
            navigate('/');
          } catch {
            setErr(t`Could not claim account. Check the email matches your existing user.`);
          } finally {
            setSubmitting(false);
          }
        }}
      >
        <h1 className="text-h1 font-semibold"><Trans>Set a password for your account</Trans></h1>
        <p className="text-body-sm text-muted">
          <Trans>Local authentication was enabled on this install. Confirm your email and choose a password to finish migrating your existing user.</Trans>
        </p>
        <Input
          placeholder={t`Email`}
          type="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <Input
          placeholder={t`New password`}
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <PasswordRequirements password={password} />
        {err && <p className="text-body text-danger">{err}</p>}
        <Button type="submit" fullWidth loading={submitting} disabled={!valid}>
          <Trans>Set password &amp; sign in</Trans>
        </Button>
      </form>
    </AuthCard>
  );
}

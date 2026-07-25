import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { useNavigate, useSearchParams } from 'react-router';
import { localAuthApi } from '../../auth/local/localAuthApi';
import { useInvitePreview } from './hooks/useInvitePreview';
import { PasswordRequirements } from '../../components/auth/PasswordRequirements';
import { Button } from '../../components/ui/Button';
import { FormField } from '../../components/ui/FormField';
import { Input } from '../../components/ui/Input';
import { LockIcon } from '../../components/icons';
import useLocalAuth from '../../hooks/useLocalAuth';
import { passwordIsValid } from '../../auth/password';

export default function Signup() {
  const { t } = useLingui();
  const [params] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- query-string param key, not UI copy
  const token = params.get('token') ?? '';
  const navigate = useNavigate();
  const { setToken } = useLocalAuth();

  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const { data: preview, isError: expired } = useInvitePreview(token);

  if (expired) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-surface p-6 text-center text-primary">
        <div>
          <h1 className="text-h1 font-semibold"><Trans>Invite expired or already used</Trans></h1>
          <p className="mt-2 text-body text-muted"><Trans>Ask an admin for a new invite link.</Trans></p>
        </div>
      </div>
    );
  }

  if (!preview) return null;

  return (
    <div className="flex min-h-screen items-center justify-center bg-surface text-primary">
      <form
        className="w-96 space-y-3 rounded-xl border border-border bg-surface-2 p-6 shadow-[var(--shadow-card)]"
        onSubmit={async (e) => {
          e.preventDefault();
          if (!passwordIsValid(password)) return;
          setSubmitting(true);
          setErr(null);
          try {
            const r = await localAuthApi.signup(token, password);
            setToken(r.token);
            navigate('/');
          } catch {
            setErr(t`Could not complete signup. The invite may have expired.`);
          } finally {
            setSubmitting(false);
          }
        }}
      >
        <h1 className="text-h1 font-semibold"><Trans>Create your account</Trans></h1>
        <p className="text-body-sm text-muted">
          <Trans>Role: <span className="text-primary">{preview.role}</span></Trans>
        </p>
        {/* Email is fixed by the invite — the backend ignores any client value and uses the
            invited address, so the field is locked here to match. */}
        <FormField label={t`Email`}>
          <Input
            value={preview.email}
            data-testid="signup-email"
            disabled
            readOnly
            rightAddon={<LockIcon size={14} />}
          />
        </FormField>
        <FormField label={t`Password`}>
          <Input
            data-testid="signup-password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </FormField>
        <PasswordRequirements password={password} />
        {err && <p data-testid="signup-error" className="text-body text-danger">{err}</p>}
        <Button data-testid="signup-submit" type="submit" fullWidth loading={submitting} disabled={!passwordIsValid(password)}>
          <Trans>Create account</Trans>
        </Button>
      </form>
    </div>
  );
}

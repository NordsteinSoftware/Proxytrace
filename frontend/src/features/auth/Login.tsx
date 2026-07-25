import { Navigate } from 'react-router';
import { useAuthMode } from '../../auth/authMode';
import { LegacyClaim } from './components/LegacyClaim';
import { LocalLogin } from './components/LocalLogin';
import { OidcLogin } from './components/OidcLogin';

export default function Login() {
  const { data } = useAuthMode();
  if (!data) return null;
  if (data.mode !== 'local') return <OidcLogin />;
  if (data.setupRequired) return <Navigate to="/setup" replace />;
  if (data.legacyClaimAvailable) return <LegacyClaim />;
  return <LocalLogin />;
}

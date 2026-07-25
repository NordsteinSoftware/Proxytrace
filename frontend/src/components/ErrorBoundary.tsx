import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { Trans } from '@lingui/react/macro';
import { Button } from './ui/Button';

interface Props {
  children: ReactNode;
  /**
   * Clears a caught error whenever any entry changes — pass `location.key` so navigating recovers.
   * Without it the only escape is "Try again", which re-renders the same children and so brings a
   * deterministic error straight back, and react-router reconciles one boundary instance across
   * routes, leaving a stale error on every page until a full reload.
   */
  resetKeys?: readonly unknown[];
  /**
   * Replaces the default full-height message. App chrome (a 48px top bar, a nav rail) needs a
   * fallback shaped like the slot it fills — see {@link ChromeErrorFallback}.
   */
  fallback?: ReactNode;
}
interface State { error: Error | null; }

function keysChanged(a: readonly unknown[] = [], b: readonly unknown[] = []): boolean {
  return a.length !== b.length || a.some((value, i) => !Object.is(value, b[i]));
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled error:', error, info.componentStack);
  }

  componentDidUpdate(prevProps: Props) {
    if (this.state.error && keysChanged(prevProps.resetKeys, this.props.resetKeys)) {
      this.setState({ error: null });
    }
  }

  render() {
    if (this.state.error) {
      if (this.props.fallback !== undefined) return this.props.fallback;
      return (
        <div className="flex flex-col items-center justify-center flex-1 gap-4 p-15 text-center">
          <div className="text-display text-muted">⚠</div>
          <div className="text-h1 font-semibold text-primary"><Trans>Something went wrong</Trans></div>
          <div className="text-title text-muted max-w-[400px] leading-relaxed">{this.state.error.message}</div>
          <Button variant="secondary" size="sm" onClick={() => this.setState({ error: null })}>
            <Trans>Try again</Trans>
          </Button>
        </div>
      );
    }
    return this.props.children;
  }
}

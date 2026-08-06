import { useAuiState } from '@assistant-ui/react';
import { Trans, useLingui } from '@lingui/react/macro';
import { ClockIcon, CoinsIcon } from '../../../components/icons';
import { fmtLatency, fmtTokens } from '../../../lib/format';
import { CachedTokensHint } from '../../../components/ui/CachedTokensHint';
import { readMessageStats, readTraceConversationId } from '../message-stats';
import { useOpenResponseTrace } from '../useOpenResponseTrace';
import { CopyMessageButton } from './CopyMessageButton';
import { OpenTraceButton } from './OpenTraceButton';

/**
 * A quiet status row beneath a finished Tracey response: the turn's input tokens, the share of that
 * input served from the provider cache, output tokens, and duration (read straight from the
 * assistant message metadata, attached on finish by {@link TraceyTransport} from the SDK's per-turn
 * usage aggregate), a copy-to-clipboard action, and a deep-link to the captured trace(s). Hidden
 * while the turn is still streaming (metadata is attached only on finish).
 */
export function MessageStatusBar() {
  const { t } = useLingui();
  // `metadata.custom` is a stable reference on the stored message, so selecting it (rather than a
  // freshly-derived object) keeps the selector snapshot stable.
  const custom = useAuiState(({ message: m }) => (m.role === 'assistant' ? m.metadata.custom : undefined));
  const text = useAuiState(({ message: m }) =>
    m.role === 'assistant' ? m.content.map((p) => (p.type === 'text' ? p.text : '')).join('').trim() : '',
  );
  const { openTrace } = useOpenResponseTrace();

  const conversationId = readTraceConversationId(custom?.traceConversationId);
  const stats = readMessageStats(custom);

  if (!conversationId) return null;

  return (
    <div data-testid="tracey-message-status" className="flex items-center gap-3 text-body-sm text-muted">
      {stats && (
        <div className="flex items-center gap-3">
          <span className="inline-flex items-center gap-1.5">
            <CoinsIcon size={12} strokeWidth={2.2} />
            <span className="inline-flex items-center gap-1" title={t`Input tokens`}>
              <span className="font-mono">{fmtTokens(stats.inputTokens)}</span>
              <Trans>in</Trans>
            </span>
            <CachedTokensHint cachedInput={stats.cachedInputTokens} input={stats.inputTokens} bare />
            <span className="inline-flex items-center gap-1" title={t`Output tokens`}>
              <span className="font-mono">{fmtTokens(stats.outputTokens)}</span>
              <Trans>out</Trans>
            </span>
          </span>
          {stats.durationMs != null && (
            <span className="inline-flex items-center gap-1" title={t`Response time`}>
              <ClockIcon size={12} strokeWidth={2.2} />
              <span className="font-mono">{fmtLatency(stats.durationMs)}</span>
            </span>
          )}
        </div>
      )}
      <div className="flex items-center gap-0.5">
        {text.length > 0 && <CopyMessageButton text={text} />}
        <OpenTraceButton onClick={() => openTrace(conversationId)} />
      </div>
    </div>
  );
}

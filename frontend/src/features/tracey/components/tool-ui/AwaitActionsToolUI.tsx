import { type ToolCallMessagePartComponent, useAuiState } from '@assistant-ui/react';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg, plural } from '@lingui/core/macro';
import { type MessageDescriptor } from '@lingui/core';
import { ClockIcon } from '../../../../components/icons';
import { Badge } from '../../../../components/ui/Badge';
import type { AnyAwaitResult, AwaitError, AwaitKind } from '../../tools/await';
import { ToolUIFrame } from './ToolUIFrame';
import { AwaitPendingRunRow } from './AwaitPendingRunRow';
import { AwaitPendingTheoryRow } from './AwaitPendingTheoryRow';
import { AwaitResultRow } from './AwaitResultRow';
import { AwaitErrorRow } from './AwaitErrorRow';
import { ElapsedStopwatch } from '../../../../components/ui/ElapsedStopwatch';
import { awaitOutcome, type AwaitOutcomeTone } from './await-card-logic';

interface AwaitActionsArgs {
  handles?: { kind?: AwaitKind; id?: string }[];
}

interface AwaitActionsResult {
  results: AnyAwaitResult[];
  errors?: AwaitError[];
  anyTimedOut: boolean;
}

/** Verdict badge copy — exhaustive over the tone, so a new tone can't silently mislabel. */
const OUTCOME_LABEL: Record<AwaitOutcomeTone, MessageDescriptor> = {
  success: msg`All done`,
  warn: msg`Check results`,
  danger: msg`Needs attention`,
};

/**
 * Inline renderer for the `await_actions` tool. While the wait runs it shows one live row per
 * handle — mirroring the SSE-patched cache the live cards maintain (case progress for runs, the
 * A/B phase for theories) — plus an elapsed stopwatch and the streaming ring. Once resolved it
 * settles into one outcome row per action (with per-case counts and timed-out / failed-handle
 * markers) under a card-level verdict badge. The per-action live cards above stream the fully
 * detailed progress.
 */
export const AwaitActionsToolUI: ToolCallMessagePartComponent = ({ args, result, status, isError }) => {
  const { t, i18n } = useLingui();
  const threadRunning = useAuiState((s) => s.thread.isRunning);
  const resolved = result as AwaitActionsResult | undefined;
  // User hit Stop while the wait was polling: the poll aborts, but the test run / theory keeps
  // running on the backend — so this is a calm "stopped", not a red error. Two shapes reach here:
  // assistant-ui sometimes finalizes the part as `incomplete/cancelled`, but when Stop lands mid
  // `execute` the aborted tool call leaves no terminal delta, so the part is orphaned with no
  // result (status stays `running`/`complete`). Once the thread is idle a result-less wait can
  // never resolve, so treat any non-error part with no result on an idle thread as stopped —
  // otherwise it would spin on "Waiting for N actions" forever.
  const stopped =
    (status.type === 'incomplete' && status.reason === 'cancelled') ||
    (status.type !== 'incomplete' && !resolved && !threadRunning);
  const errored = !stopped && (isError || status.type === 'incomplete');

  if (stopped) {
    return (
      <ToolUIFrame state="ready" icon={<ClockIcon size={14} />} title={t`Wait stopped`} testId="tracey-await-card">
        <span className="text-body-sm text-muted">
          <Trans>You stopped before these finished — they keep running in the background.</Trans>
        </span>
      </ToolUIFrame>
    );
  }
  if (errored) {
    return <ToolUIFrame state="error" testId="tracey-await-card" />;
  }

  // Stopped and errored have returned, so a missing result here means the wait is still running.
  if (!resolved) {
    const { handles } = args as AwaitActionsArgs;
    const pending = (handles ?? []).filter((h): h is { kind: AwaitKind; id: string } => !!h.kind && !!h.id);
    const waitingTitle = pending.length
      ? plural(pending.length, { one: 'Waiting for # action', other: 'Waiting for # actions' })
      : t`Waiting for … actions`;
    return (
      <ToolUIFrame
        state="ready"
        icon={<ClockIcon size={14} />}
        title={waitingTitle}
        live
        cornerAccessory={<ElapsedStopwatch />}
        testId="tracey-await-card"
      >
        <div className="flex flex-col gap-2" aria-busy="true">
          {pending.map((handle) =>
            handle.kind === 'test-run' ? (
              <AwaitPendingRunRow key={`${handle.kind}:${handle.id}`} id={handle.id} />
            ) : (
              <AwaitPendingTheoryRow key={`${handle.kind}:${handle.id}`} id={handle.id} />
            ),
          )}
          <span className="border-t border-hairline pt-2 text-caption text-muted">
            <Trans>Tracey picks up the moment everything finishes.</Trans>
          </span>
        </div>
      </ToolUIFrame>
    );
  }

  const outcome = awaitOutcome(resolved.results, resolved.errors, resolved.anyTimedOut);
  return (
    <ToolUIFrame
      state="ready"
      icon={<ClockIcon size={14} />}
      title={t`Awaited actions`}
      cornerAccessory={<Badge label={i18n._(OUTCOME_LABEL[outcome])} variant={outcome} size="sm" />}
      testId="tracey-await-card"
    >
      <div className="flex flex-col gap-2">
        {resolved.results.map((item, index) => (
          <AwaitResultRow key={`${item.kind}:${item.id}`} item={item} delayIndex={index} />
        ))}
        {(resolved.errors ?? []).map((item, index) => (
          <AwaitErrorRow
            key={`${item.kind}:${item.id}`}
            item={item}
            delayIndex={resolved.results.length + index}
          />
        ))}
      </div>
    </ToolUIFrame>
  );
};

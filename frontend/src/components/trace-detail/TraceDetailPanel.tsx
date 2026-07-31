import { useState } from 'react';
import { useNavigate } from 'react-router';
import type { AgentCallDto, MessageDto } from '../../api/models';
import { useAgentSuites } from './usePromoteTrace';
import { fmtLatency, fmtTokens, cachedPct } from '../../lib/format';
import {
  ClockIcon, CoinsIcon,
  ArrowDownToLineIcon, ArrowUpFromLineIcon, ServerIcon,
} from '../icons';
import { ToolMessageBubble } from '../ui/ToolMessageBubble';
import { Button } from '../ui/Button';
import { Tabs } from '../ui/Tabs';
import { DetailPanel } from '../overlays/DetailPanel';
import { PromoteModal } from './PromoteModal';
import { SynthesizeTestsModal } from './SynthesizeTestsModal';
import { AskTraceyModal } from './AskTraceyModal';
import { DrawerStat } from './DrawerStat';
import { TraceAnomalyBanner } from './TraceAnomalyBanner';
import { TraceDetailHeader } from './TraceDetailHeader';
import { TraceMessagesTab } from './TraceMessagesTab';
import { TraceRawJsonTab, TraceMetadataTab } from './TraceMetadataTab';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import { type MessageDescriptor } from '@lingui/core';
import { useTraceyChatContext } from '../../features/tracey/tracey-chat-context';
import { traceQuestionPrompt } from '../tracey/askTraceyPrompts';

type Tab = 'Messages' | 'Tools' | 'Raw JSON' | 'Metadata';

const TAB_LABELS: Record<Tab, MessageDescriptor> = {
  Messages: msg`Messages`,
  Tools: msg`Tools`,
  'Raw JSON': msg`Raw JSON`,
  Metadata: msg`Metadata`,
};

interface Props {
  trace: AgentCallDto;
  onClose: () => void;
  onPrev?: () => void;
  onNext?: () => void;
}

export function TraceDetailPanel({ trace, onClose, onPrev, onNext }: Props) {
  const navigate = useNavigate();
  const { askTracey } = useTraceyChatContext();
  const { t, i18n } = useLingui();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- Tab id token (display label from TAB_LABELS)
  const [tab, setTab] = useState<Tab>('Messages');
  const [promoting, setPromoting] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [askingTracey, setAskingTracey] = useState(false);
  const [prevTraceId, setPrevTraceId] = useState(trace.id);

  // Reset tab when trace changes (derived state pattern per BEST_PRACTICES §4)
  if (prevTraceId !== trace.id) {
    setPrevTraceId(trace.id);
    // eslint-disable-next-line lingui/no-unlocalized-strings -- Tab id token (display label from TAB_LABELS)
    setTab('Messages');
    setPromoting(false);
    setGenerating(false);
    setAskingTracey(false);
  }

  const suitesQuery = useAgentSuites(trace.agentId);
  const suites = suitesQuery.data?.items ?? [];
  const hasResponse = !!trace.response;
  const promoteDisabled = !trace.agentId || !hasResponse || suitesQuery.isLoading || suites.length === 0;
  const promoteTooltip = !trace.agentId
    ? t`This trace is not linked to an agent and cannot be promoted.`
    : !hasResponse
      ? t`This trace has no response and cannot be promoted.`
      : suitesQuery.isLoading
        ? t`Loading test suites…`
        : suites.length === 0
          ? t`No test suite for this agent yet — create one on the Test Suites page first.`
          : '';

  const tokTotal = trace.inputTokens + trace.outputTokens;
  const cachePct = cachedPct(trace.cachedInputTokens, trace.inputTokens);

  const allMessages: MessageDto[] = [...trace.request, ...(trace.response ? [trace.response] : [])];
  const msgCount = allMessages.length;

  const toolResultByCallId = new Map<string, MessageDto>();
  for (const m of allMessages) {
    if (m.role === 'tool' && m.toolCallId) toolResultByCallId.set(m.toolCallId, m);
  }
  const invocations = allMessages.flatMap(m =>
    (m.toolRequests ?? []).map(req => ({ req, result: toolResultByCallId.get(req.id) })),
  );

  const jumpToDefinition = (toolName: string) => {
    if (!trace.agentId) return;
    onClose();
    navigate(`/agents?id=${trace.agentId}&tool=${encodeURIComponent(toolName)}`);
  };

  const TABS: [Tab, number | null][] = [
    ['Messages', msgCount],
    ['Tools', invocations.length],
    ['Raw JSON', null],
    ['Metadata', null],
  ];

  return (
    <>
      <DetailPanel onClose={onClose} onPrev={onPrev} onNext={onNext} keyboardEnabled={!promoting && !generating && !askingTracey} testId="trace-detail">
        <TraceDetailHeader
          trace={trace}
          onClose={onClose}
          onPrev={onPrev}
          onNext={onNext}
          onAskTracey={() => setAskingTracey(true)}
          promote={{
            disabled: promoteDisabled,
            tooltip: promoteTooltip,
            onStart: () => setPromoting(true),
          }}
          generate={{
            // Same preconditions as promotion: an agent, a response, and somewhere to put the cases.
            disabled: promoteDisabled,
            tooltip: promoteTooltip,
            onStart: () => setGenerating(true),
          }}
        />

        <TraceAnomalyBanner trace={trace} />

        {/* Stat band */}
        <div className="mx-5 mt-3.5 px-4 py-3.5 bg-card-2 grid grid-cols-[repeat(auto-fit,minmax(90px,1fr))] gap-3.5 shrink-0 shadow-[inset_0_0_0_1px_var(--hairline)]">
          {/* eslint-disable-next-line lingui/no-unlocalized-strings -- DrawerStat tone enum values, not UI copy */}
          <DrawerStat label={t`Latency`} value={fmtLatency(trace.durationMs)} icon={<ClockIcon size={15} strokeWidth={2.2} />} tone={trace.durationMs > 3000 ? 'warn' : 'info'} valueTone={trace.durationMs > 3000 ? 'warn' : undefined} />
          <DrawerStat label={t`Input`} value={fmtTokens(trace.inputTokens)} icon={<ArrowDownToLineIcon size={15} strokeWidth={2.2} />} tone="info" />
          <DrawerStat label={t`Output`} value={fmtTokens(trace.outputTokens)} icon={<ArrowUpFromLineIcon size={15} strokeWidth={2.2} />} tone="success" />
          <DrawerStat
            label={t`Cached`}
            value={cachePct !== null ? `${cachePct}%` : '—'}
            icon={<ServerIcon size={15} strokeWidth={2.2} />}
            tone="accent"
          />
          <DrawerStat
            label={t`Cost`}
            // eslint-disable-next-line lingui/no-unlocalized-strings -- test id, not UI copy
            valueTestId={`trace-cost-${trace.id}`}
            value={trace.costEur != null ? `€${trace.costEur.toFixed(4)}` : '—'}
            icon={<CoinsIcon size={15} strokeWidth={2.2} />}
            tone="warn"
            sub={trace.costEur == null
              ? <Button variant="link" className="text-caption" onClick={() => { onClose(); navigate('/settings/providers'); }} title={t`Configure pricing for this model endpoint`}><Trans>Set price →</Trans></Button>
              : undefined}
          />
        </div>

        {/* Tabs */}
        <Tabs
          className="px-5 pt-3.5 shrink-0"
          value={tab}
          onChange={t => setTab(t as Tab)}
          items={TABS.map(([t, count]) => ({
            value: t,
            'data-testid': `trace-tab-${t.toLowerCase().replace(/\s+/g, '-')}`,
            label: (
              <span className="inline-flex items-center gap-1.5">
                {i18n._(TAB_LABELS[t])}
                {count !== null && (
                  <span className="px-1.5 py-px rounded-none text-caption font-mono font-semibold bg-card-2 text-muted group-data-[state=active]:bg-accent-subtle group-data-[state=active]:text-accent-hover">{count}</span>
                )}
              </span>
            ),
          }))}
        />

        {/* Tab body */}
        <div
          data-testid={`trace-${tab.toLowerCase().replace(/\s+/g, '-')}-tab`}
          className="flex-1 min-h-0 overflow-y-auto px-5 pt-3.5 pb-7 flex flex-col gap-2.5 [&>*]:shrink-0"
        >
          {tab === 'Messages' && (
            <TraceMessagesTab trace={trace} onJumpToDefinition={jumpToDefinition} />
          )}
          {tab === 'Tools' && (
            invocations.length === 0
              ? <div className="px-5 py-10 text-center text-muted text-body"><Trans>No tools were invoked in this trace.</Trans></div>
              : invocations.map(({ req, result }, i) => (
                <ToolMessageBubble
                  key={`${req.id}-${i}`}
                  request={req}
                  result={result}
                  onJumpToDefinition={trace.agentId ? () => jumpToDefinition(req.name) : undefined}
                />
              ))
          )}
          {tab === 'Raw JSON' && <TraceRawJsonTab trace={trace} tokTotal={tokTotal} />}
          {tab === 'Metadata' && <TraceMetadataTab trace={trace} />}
        </div>
      </DetailPanel>

      {promoting && <PromoteModal trace={trace} suites={suites} onClose={() => setPromoting(false)} />}
      {generating && (
        <SynthesizeTestsModal trace={trace} suites={suites} onClose={() => setGenerating(false)} />
      )}
      {askingTracey && (
        <AskTraceyModal
          traceId={trace.id}
          onClose={() => setAskingTracey(false)}
          onSubmit={question => {
            setAskingTracey(false);
            askTracey(traceQuestionPrompt(trace.id, question));
          }}
        />
      )}
    </>
  );
}

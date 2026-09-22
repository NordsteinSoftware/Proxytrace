import { useState } from 'react';
import { Button } from '../../../components/ui/Button';
import { CodeBlock } from '../../../components/ui/CodeBlock';
import { Tabs } from '../../../components/ui/Tabs';
import { Modal } from '../../../components/overlays/Modal';
import useCurrentProject from '../../../hooks/useCurrentProject';
import { useIngestionBase } from '../../../hooks/useIngestionBase';
import { ingestionUrl } from '../../../lib/ingestion';
import { buildQuickStartSnippets, type SnippetLanguage } from '../../../lib/ingestionSnippets';
import { Trans, useLingui } from '@lingui/react/macro';

/** Illustrative model id used in the quick-start snippet (the user picks their own). */
// eslint-disable-next-line lingui/no-unlocalized-strings -- sample model id, not UI copy
const SAMPLE_MODEL = 'gpt-4o-mini';

function InstrumentationInstructions() {
  const { t } = useLingui();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- code snippet language id, not UI copy
  const [lang, setLang] = useState<SnippetLanguage>('python');
  const { currentProject } = useCurrentProject();
  const proxyBase = useIngestionBase();

  const projectName = currentProject?.name ?? '';
  const baseUrl = projectName ? ingestionUrl(projectName, proxyBase) : '';
  const snippets = buildQuickStartSnippets(baseUrl, SAMPLE_MODEL);
  const active = snippets.find(s => s.id === lang) ?? snippets[0];

  return (
    <div data-testid="traces-instrumentation-instructions" className="w-full max-w-2xl flex flex-col gap-4">
      <div className="flex flex-col gap-1 text-center">
        <span className="text-body text-secondary max-w-prose">
          <Trans>
            Route your agent through the proxy and every LLM call lands here automatically. Point
            your OpenAI client at this project's <span className="font-mono text-accent-text">base_url</span> —
            keep your existing provider API key — and nothing else changes.
          </Trans>
        </span>
        <span className="text-body-sm text-secondary max-w-prose">
          <Trans>
            Tip: also send an <span className="font-mono text-accent-text">x-proxytrace-agent</span> header
            naming your agent — calls are then attributed to it deterministically instead of by
            prompt similarity.
          </Trans>
        </span>
      </div>

      {baseUrl && (
        <div className="w-full max-w-2xl flex flex-col gap-3 text-left">
          <CodeBlock heading={t`Your project's OpenAI base_url`} content={baseUrl} maxLines={1} />

          <div className="flex flex-col gap-2">
            <Tabs
              value={active.id}
              onChange={v => setLang(v as SnippetLanguage)}
              items={snippets.map(s => ({
                value: s.id,
                label: s.label,
                'data-testid': `traces-snippet-tab-${s.id}`,
              }))}
            />
            <CodeBlock content={active.code} language={active.language} maxLines={14} />
          </div>
        </div>
      )}

      <Button variant="link" asChild>
        <a
          data-testid="traces-proxy-docs-link"
          href="/docs/guide/proxy-setup.html"
          target="_blank"
          rel="noopener noreferrer"
        >
          <Trans>Full proxy setup guide →</Trans>
        </a>
      </Button>
    </div>
  );
}

export function TraceInstrumentationHelp() {
  const { t } = useLingui();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        variant="secondary"
        size="sm"
        className="order-last size-9 !p-0 rounded-full text-title"
        data-testid="traces-instrumentation-help"
        aria-label={t`Instrumentation instructions`}
        title={t`Instrumentation instructions`}
        onClick={() => setOpen(true)}
      >
        ?
      </Button>
      {open && (
        <Modal title={t`Instrument your agent`} size="md" onClose={() => setOpen(false)}>
          <InstrumentationInstructions />
        </Modal>
      )}
    </>
  );
}

/** Onboarding panel shown when a project has no traces yet. */
export function TracesEmptyState() {
  return (
    <div
      data-testid="traces-empty-state"
      className="py-10 px-4 flex flex-col items-center gap-4 text-center"
    >
      <span className="text-h2 font-semibold text-primary"><Trans>No traces yet</Trans></span>
      <InstrumentationInstructions />
    </div>
  );
}

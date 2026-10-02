import { useLingui } from '@lingui/react/macro';
import { useIngestionBase } from '../../../hooks/useIngestionBase';
import useCurrentProject from '../../../hooks/useCurrentProject';
import { ingestionUrl, scopeHeaderLine } from '../../../lib/ingestion';
import { CodeBlock } from '../../../components/ui/CodeBlock';

/**
 * The two ways a client sends traffic into a scope: the scoped base URL (configure once), or the
 * project base URL plus the `x-proxytrace-scope` header (choose per request). Both copyable.
 */
export function ScopeBaseUrl({ scopeKey }: { scopeKey: string }) {
  const { t } = useLingui();
  const { currentProject } = useCurrentProject();
  const proxyBase = useIngestionBase();
  const projectName = currentProject?.name ?? '';
  if (!projectName) return null;

  return (
    <div className="flex flex-col gap-2.5" data-testid="scope-base-url">
      <CodeBlock heading={t`Scoped OpenAI base_url`} content={ingestionUrl(projectName, proxyBase, scopeKey)} maxLines={1} />
      <CodeBlock
        heading={t`Or send the header with the project base_url`}
        content={scopeHeaderLine(scopeKey)}
        maxLines={1}
      />
    </div>
  );
}

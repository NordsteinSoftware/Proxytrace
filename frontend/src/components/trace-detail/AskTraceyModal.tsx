import { useRef, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { ZapFilledIcon } from '../icons';
import { Button } from '../ui/Button';
import { FormField } from '../ui/FormField';
import { Textarea } from '../ui/Textarea';
import { Modal } from '../overlays/Modal';

interface Props {
  traceId: string;
  onClose: () => void;
  onSubmit: (question: string) => void;
}

export function AskTraceyModal({ traceId, onClose, onSubmit }: Props) {
  const { t } = useLingui();
  const [question, setQuestion] = useState('');
  const questionRef = useRef<HTMLTextAreaElement>(null);
  const canSubmit = question.trim().length > 0;

  const submit = () => {
    if (canSubmit) onSubmit(question);
  };

  return (
    <Modal
      title={t`Ask Tracey about this trace`}
      onClose={onClose}
      initialFocusRef={questionRef}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}><Trans>Cancel</Trans></Button>
          <Button
            variant="primary"
            data-testid="ask-tracey-submit-btn"
            disabled={!canSubmit}
            onClick={submit}
            leftIcon={<ZapFilledIcon size={12} />}
          >
            <Trans>Ask Tracey</Trans>
          </Button>
        </>
      }
    >
      <div data-testid="ask-tracey-modal" className="flex flex-col gap-3">
        <p className="text-body text-secondary">
          <Trans>Ask a specific question and Tracey will inspect the full trace before answering.</Trans>
        </p>
        <FormField label={t`Question`} htmlFor="ask-tracey-question" required>
          <Textarea
            ref={questionRef}
            id="ask-tracey-question"
            data-testid="ask-tracey-question-input"
            value={question}
            onChange={event => setQuestion(event.target.value)}
            onKeyDown={event => {
              if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
                event.preventDefault();
                submit();
              }
            }}
            rows={5}
            required
            placeholder={t`Why was the refund approved?`}
          />
        </FormField>
        <p className="font-mono text-caption text-muted break-all">
          <Trans>Trace {traceId}</Trans>
        </p>
      </div>
    </Modal>
  );
}

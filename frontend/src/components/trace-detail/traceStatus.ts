import type { MessageDescriptor } from '@lingui/core';
import { msg } from '@lingui/core/macro';

const STATUS_LABELS: Partial<Record<number, MessageDescriptor>> = {
  401: msg`Authentication failed`,
  403: msg`Access denied`,
  429: msg`Rate limited`,
};

const STATUS_CLASSES: Partial<Record<number, MessageDescriptor>> = {
  1: msg`Informational`,
  2: msg`OK`,
  3: msg`Redirect`,
  4: msg`Client error`,
  5: msg`Server error`,
};

export function traceStatusLabel(status: number): MessageDescriptor {
  return STATUS_LABELS[status] ?? STATUS_CLASSES[Math.floor(status / 100)] ?? msg`Unknown status`;
}

import { useState } from 'react';
import { useLingui } from '@lingui/react/macro';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { providersApi } from '../../../api/providers';
import { setupApi } from '../../../api/setup';
import { QUERY_KEYS } from '../../../api/query-keys';
import type {
  CreateApiKeyRequest, CreateProviderRequest,
  ModelProviderKind, ProviderDto,
} from '../../../api/models';
import useToast from '../../../hooks/useToast';
import { normalizeProviderConnectionError, ProviderConnectionTestError } from '../../../lib/providerConnection';

/** Creates a provider; invalidates the list. Caller selects the new provider via `mutate`'s onSuccess. */
export function useCreateProvider() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateProviderRequest) => providersApi.create(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/** Updates a provider's kind, preserving its other fields. */
export function useUpdateProviderKind() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (args: { provider: { id: string; name: string; endpoint: string }; kind: ModelProviderKind }) =>
      providersApi.update(args.provider.id, {
        name: args.provider.name,
        endpoint: args.provider.endpoint,
        // null = leave the stored credential alone. The client does not hold the upstream key, so
        // there is nothing to echo back on an edit that only changes the kind.
        upstreamApiKey: null,
        kind: args.kind,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/**
 * Fetches a provider's upstream credential on demand.
 *
 * Deliberately a mutation, not a query: the key must never enter the Query cache, where it would
 * outlive the moment the user asked for it and be visible to anything reading the cache. The server
 * audits every call, so it is only invoked on an explicit reveal or copy.
 */
export function useRevealUpstreamKey(providerId: string) {
  return useMutation({
    mutationFn: () => providersApi.getUpstreamKey(providerId).then(r => r.upstreamApiKey),
  });
}

/** Verifies a replacement upstream key before persisting it. */
export function useRotateUpstreamKey(provider: ProviderDto) {
  const { t } = useLingui();
  const { show: toast } = useToast();
  const qc = useQueryClient();
  const [phase, setPhase] = useState<'verifying' | 'saving' | null>(null);
  const mutation = useMutation({
    mutationFn: async (newKey: string) => {
      setPhase('verifying');
      const verification = await setupApi.testConnection({
        providerName: provider.name,
        providerEndpoint: provider.endpoint,
        providerUpstreamApiKey: newKey,
        providerKind: provider.kind,
      });
      if (!verification.success) {
        throw new ProviderConnectionTestError(
          normalizeProviderConnectionError(verification.errorCode),
          verification.error,
          verification.errorId,
        );
      }

      setPhase('saving');
      const saved = await providersApi.update(provider.id, {
        name: provider.name,
        endpoint: provider.endpoint,
        upstreamApiKey: newKey,
        kind: provider.kind,
      });

      return { provider: saved, modelCount: verification.modelCount };
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview });
      toast(t`Upstream API key updated.`, 'success');
    },
    onSettled: () => setPhase(null),
  });

  return { ...mutation, phase };
}

/** Deletes a provider; invalidates the list. Caller resets selection via `mutate`'s onSuccess. */
export function useDeleteProvider() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (providerId: string) => providersApi.delete(providerId),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/** Deletes a model endpoint by id. */
export function useDeleteModel() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (endpointId: string) => providersApi.deleteModel(endpointId),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/** Generates a new Proxytrace API key for a provider. */
export function useCreateKey(providerId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateApiKeyRequest) => providersApi.createKey(providerId, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/** Revokes an API key by id. */
export function useDeleteKey(providerId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (keyId: string) => providersApi.deleteKey(providerId, keyId),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

/** Re-discovers a provider's models and refreshes pricing; invalidates the overview. */
export function useReloadProvider(providerId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => providersApi.reload(providerId),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.providersOverview }),
  });
}

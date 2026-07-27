import { api, type RequestOptions } from './client';
import type {
  ApiKeyDto, CreateApiKeyRequest, CreateProviderRequest,
  ModelEndpointDto, ModelProviderKind,
  ProviderDto, ProviderKeyDto, ProvidersOverviewDto,
} from './models';

export const providersApi = {
  /** Page payload: every provider with embedded models + keys, plus projects, in one request. */
  overview: () => api.get<ProvidersOverviewDto>('/api/providers/overview'),
  get: (id: string, opts?: RequestOptions) => api.get<ProviderDto>(`/api/providers/${id}`, opts),
  /**
   * Fetches a provider's upstream credential in the clear. Deliberately a separate request rather
   * than a field on the provider: the key used to ship with every providers-page load, so nothing
   * recorded who had actually seen it. The server audits each call — only ask when the user asks.
   */
  getUpstreamKey: (id: string) => api.get<ProviderKeyDto>(`/api/providers/${id}/key`),
  create: (req: CreateProviderRequest) => api.post<ProviderDto>('/api/providers', req),
  /** `upstreamApiKey: null` leaves the stored credential untouched — the client never holds it. */
  update: (id: string, req: { name: string; endpoint: string; upstreamApiKey: string | null; kind: ModelProviderKind }) =>
    api.put<ProviderDto>(`/api/providers/${id}`, req),
  delete: (id: string) => api.del(`/api/providers/${id}`),

  getAllModels: () => api.get<ModelEndpointDto[]>('/api/model-endpoints'),
  deleteModel: (endpointId: string) =>
    api.del(`/api/providers/endpoints/${endpointId}`),
  reload: (providerId: string) =>
    api.post<ModelEndpointDto[]>(`/api/providers/${providerId}/reload`, {}),

  createKey: (providerId: string, req: CreateApiKeyRequest) =>
    api.post<ApiKeyDto>(`/api/providers/${providerId}/keys`, req),
  deleteKey: (providerId: string, keyId: string) =>
    api.del(`/api/providers/${providerId}/keys/${keyId}`),
};

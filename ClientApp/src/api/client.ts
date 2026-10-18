export interface ApiPage<T> {
  items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number;
}
export interface ProblemDetails {
  status?: number; title?: string; detail?: string; type?: string; instance?: string;
  code?: string; traceId?: string; errors?: Record<string, string[]>;
}
export class ApiError extends Error {
  constructor(public status: number, public problem: ProblemDetails) {
    super(problem.title ?? `Request failed (${status}).`);
    this.name = 'ApiError';
  }
}
export function parseProblem(value: unknown): ProblemDetails {
  if (!value || typeof value !== 'object') return {};
  const data = value as Record<string, unknown>;
  const result: ProblemDetails = {};
  for (const field of ['title', 'detail', 'type', 'instance', 'code', 'traceId'] as const) {
    if (typeof data[field] === 'string') result[field] = data[field];
  }
  if (typeof data.status === 'number') result.status = data.status;
  if (data.errors && typeof data.errors === 'object' && !Array.isArray(data.errors)) {
    result.errors = Object.fromEntries(Object.entries(data.errors).filter(
      (entry): entry is [string, string[]] => Array.isArray(entry[1]) && entry[1].every(item => typeof item === 'string'),
    ));
  }
  return result;
}
export interface RequestOptions {
  method?: 'GET' | 'HEAD' | 'OPTIONS' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
}
// Restrict credentials and request tokens to our API. Tokens live only in a local variable.
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const pathname = path.split(/[?#]/)[0] ?? '';
  if (!path.startsWith('/api/v1/') || /[\\\r\n\t#]/.test(path) || /%2f|%5c/i.test(pathname)
    || new URL(path, 'https://rentright.invalid').pathname !== pathname) throw new Error('Expected a same-origin API path.');
  const method = options.method ?? 'GET';
  const headers = new Headers({ Accept: 'application/json' });
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    const token = await apiRequest<{ requestToken: string }>('/api/v1/session/antiforgery', { signal: options.signal });
    if (!token.requestToken) throw new Error('Unable to prepare the request.');
    headers.set('X-CSRF-TOKEN', token.requestToken);
  }
  if (options.body !== undefined) headers.set('Content-Type', 'application/json');
  const response = await fetch(path, {
    method, headers, credentials: 'same-origin', signal: options.signal,
    redirect: 'error', cache: 'no-store',
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });
  const isJson = /\bapplication\/(?:[\w.-]+\+)?json\b/i.test(response.headers.get('Content-Type') ?? '');
  if (!response.ok) {
    let problem: ProblemDetails = {};
    if (isJson) {
      try { problem = parseProblem(await response.json()); }
      catch (error) { if (options.signal?.aborted) throw error; }
    }
    throw new ApiError(response.status, problem);
  }
  if (response.status === 204 || method === 'HEAD') return undefined as T;
  if (!isJson) throw new ApiError(response.status, { title: 'The server returned an unexpected response.' });
  return await response.json() as T;
}

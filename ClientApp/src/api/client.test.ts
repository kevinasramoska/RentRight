import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, ApiError, parseProblem } from './client';
afterEach(() => vi.unstubAllGlobals());
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });
describe('apiRequest', () => {
  it.each(['GET', 'HEAD', 'OPTIONS'] as const)('uses cookies and no CSRF bootstrap for %s', async method => {
    const fetcher = vi.fn().mockResolvedValue(json({ items: [] }));
    vi.stubGlobal('fetch', fetcher);
    await apiRequest('/api/v1/items', { method });
    expect(fetcher).toHaveBeenCalledTimes(1);
    const init = fetcher.mock.calls[0]?.[1] as RequestInit;
    expect(init.credentials).toBe('same-origin');
    expect(init.redirect).toBe('error');
    expect(new Headers(init.headers).has('X-CSRF-TOKEN')).toBe(false);
  });
  it.each(['POST', 'PUT', 'PATCH', 'DELETE'] as const)('bootstraps a token before %s', async method => {
    const fetcher = vi.fn().mockResolvedValueOnce(json({ requestToken: 'test-token' })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetcher);
    const controller = new AbortController();
    await apiRequest('/api/v1/items', { method, body: { title: 'Example' }, signal: controller.signal });
    expect(fetcher.mock.calls[0]?.[0]).toBe('/api/v1/session/antiforgery');
    const init = fetcher.mock.calls[1]?.[1] as RequestInit;
    expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('test-token');
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json');
    expect(init.body).toBe('{"title":"Example"}');
    expect(init.credentials).toBe('same-origin');
    expect(init.signal).toBe(controller.signal);
  });
  it('parses validation ProblemDetails without discarding field errors', async () => {
    const problem = { title: 'Invalid', code: 'validation_failed', traceId: 'trace', errors: { title: ['Required'] } };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(problem), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })));
    await expect(apiRequest('/api/v1/items')).rejects.toMatchObject({ status: 400, problem });
  });
  it('sends a version precondition only on the mutation, not the token bootstrap', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(json({ requestToken: 'test-token' })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetcher);
    await apiRequest('/api/v1/properties/7', { method: 'DELETE', expectedVersion: 'AAAAAAAAAAE=' });
    expect(new Headers(fetcher.mock.calls[0]![1].headers).has('X-Expected-Version')).toBe(false);
    expect(new Headers(fetcher.mock.calls[1]![1].headers).get('X-Expected-Version')).toBe('AAAAAAAAAAE=');
  });
  it.each(['<html>Private server error</html>', '{broken'])('uses a safe error for non-JSON or malformed failures', async body => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body, { status: 500, headers: { 'Content-Type': body.startsWith('{') ? 'application/json' : 'text/html' } })));
    await expect(apiRequest('/api/v1/items')).rejects.toMatchObject({ message: 'Request failed (500).', problem: {} });
  });
  it('rejects an unexpected success response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('Login', { headers: { 'Content-Type': 'text/html' } })));
    await expect(apiRequest('/api/v1/items')).rejects.toBeInstanceOf(ApiError);
  });
  it('preserves cancellation', async () => {
    const error = new DOMException('Aborted', 'AbortError');
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(error));
    await expect(apiRequest('/api/v1/items', { signal: AbortSignal.abort() })).rejects.toBe(error);
  });
  it('does not send a mutation when bootstrap fails', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({}, 403));
    vi.stubGlobal('fetch', fetcher);
    await expect(apiRequest('/api/v1/items', { method: 'POST' })).rejects.toBeInstanceOf(ApiError);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it.each(['https://other.example/api/v1/items', '//other.example/api/v1/items', '/api/v1/\\other',
    '/api/v1/../../Identity/Account/Logout', '/api/v1/%2e%2e/session', '/api/v1/%2e%2e%2fprivate',
    '/api/v1/%5cother', '/api/v1/items#ignored', '/api/v1/ite\tms'])('rejects external or ambiguous paths: %s', async path => {
    const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
    await expect(apiRequest(path)).rejects.toThrow('same-origin');
    expect(fetcher).not.toHaveBeenCalled();
  });
  it('does not retry a rate-limited mutation or bootstrap it again', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(json({ requestToken: 'test-token' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'rate_limited', title: 'Too many requests' }),
        { status: 429, headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '60' } }));
    vi.stubGlobal('fetch', fetcher);
    await expect(apiRequest('/api/v1/items', { method: 'POST' })).rejects.toMatchObject({ status: 429, problem: { code: 'rate_limited' } });
    expect(fetcher).toHaveBeenCalledTimes(2);
  });
});
it('filters malformed problem fields', () => {
  expect(parseProblem({ title: 42, errors: { valid: ['Required'], invalid: [42] } })).toEqual({ errors: { valid: ['Required'] } });
  expect(parseProblem(null)).toEqual({});
});

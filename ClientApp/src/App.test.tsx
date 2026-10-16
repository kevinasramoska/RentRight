import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import App from './App';
import { EmptyState, ErrorState, LoadingState } from './components/States';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it('announces loading and exposes an actionable error', () => {
  const retry = vi.fn();
  render(<><LoadingState /><ErrorState onRetry={retry} /></>);
  expect(screen.getByRole('status').textContent).toContain('Loading');
  expect(screen.getByRole('alert')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
  expect(retry).toHaveBeenCalledOnce();
});
it('escapes server text in empty states', () => {
  const { container } = render(<EmptyState message={'<script>alert(1)</script>'} />);
  expect(container.querySelector('script')).toBeNull();
  expect(container.textContent).toBe('<script>alert(1)</script>');
});
it('provides navigation, a main landmark, and explicit anonymous state', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"isAuthenticated":false,"roles":[]}', { headers: { 'Content-Type': 'application/json' } })));
  render(<App />);
  expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeTruthy();
  expect(screen.getByRole('main').id).toBe('main');
  expect(await screen.findByText('Sign in to access your RentRight account.')).toBeTruthy();
});
it('loads an authenticated session without displaying account identifiers', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"isAuthenticated":true,"roles":["Tenant"]}', { headers: { 'Content-Type': 'application/json' } })));
  render(<App />);
  expect(await screen.findByText('You’re signed in.')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Dashboard' }).getAttribute('href')).toBe('/Dashboard');
});
it('retries a failed session request', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValueOnce(new TypeError('Network failure')).mockResolvedValueOnce(new Response('{"isAuthenticated":false,"roles":[]}', { headers: { 'Content-Type': 'application/json' } })));
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
  expect(await screen.findByText('Sign in to access your RentRight account.')).toBeTruthy();
});

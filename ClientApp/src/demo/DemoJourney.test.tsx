import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import App from '../App';

const fetchMock = vi.fn();
beforeEach(() => {
  history.replaceState(null, '', '/app/demo/');
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response(JSON.stringify({ isAuthenticated: false, roles: [] }), { headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const guide = () => screen.getByRole('region', { name: 'A home. An issue. A clear next step.' });
function follow(name: RegExp) {
  if (!screen.queryByRole('navigation', { name: 'Demo walkthrough' })) fireEvent.click(screen.getByRole('button', { name: 'Show walkthrough' }));
  fireEvent.click(within(guide()).getByRole('link', { name }));
}

it('walks through the seeded home, list, timeline and submission without live API calls', async () => {
  render(<App />);
  await screen.findByRole('heading', { name: 'The Willow, Apartment 4B' });
  expect(screen.queryByRole('navigation', { name: 'Demo walkthrough' })).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Show walkthrough' }));
  expect(within(guide()).getByRole('link', { name: /Your home/ }).getAttribute('aria-current')).toBe('step');
  follow(/Browse requests/);
  await screen.findByRole('heading', { name: /All requests/ });
  expect(within(guide()).getByRole('link', { name: /Browse requests/ }).getAttribute('aria-current')).toBe('step');
  follow(/Follow a repair/);
  await screen.findByRole('heading', { name: 'Status timeline' });
  expect(screen.getByText(/A maintenance visit has been arranged/)).toBeTruthy();
  expect(within(guide()).getByRole('link', { name: /Follow a repair/ }).getAttribute('aria-current')).toBe('step');
  follow(/Report an issue/);
  const input = await screen.findByRole('textbox', { name: 'Description' });
  expect(within(guide()).getByRole('link', { name: /Report an issue/ }).getAttribute('aria-current')).toBe('step');
  fireEvent.change(input, { target: { value: 'A fictional kitchen drawer is sticking.' } });
  fireEvent.submit(input.closest('form')!);
  await screen.findByRole('heading', { name: 'Request #1049' });
  follow(/Your home/);
  await screen.findByRole('heading', { name: 'Your active tenancy' });
  expect(screen.getByRole('region', { name: 'Maintenance summary' }).textContent).toContain('4 open requests');
  expect(fetchMock).not.toHaveBeenCalled();
});

it('requires confirmation before discarding a form and restores focus when cancelled', async () => {
  history.replaceState(null, '', '/app/demo/requests/new');
  render(<App />);
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'An unfinished fictional report.' } });
  const reset = screen.getByRole('button', { name: 'Reset demo' });
  fireEvent.click(reset);
  expect(reset.getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByRole('group', { name: 'Start fresh?' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Keep exploring' }));
  expect(document.activeElement).toBe(reset);
  expect((input as HTMLTextAreaElement).value).toBe('An unfinished fictional report.');
  expect(location.pathname).toBe('/app/demo/requests/new');
  expect(screen.queryByRole('group', { name: 'Start fresh?' })).toBeNull();
  fireEvent.click(reset);
  fireEvent.click(screen.getByRole('button', { name: 'Reset and start again' }));
  await screen.findByRole('heading', { name: 'The Willow, Apartment 4B' });
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Overview' }));
  follow(/Report an issue/);
  expect((await screen.findByRole('textbox', { name: 'Description' }) as HTMLTextAreaElement).value).toBe('');
  expect(fetchMock).not.toHaveBeenCalled();
});

it('resets simulated requests on the same overview route and removes their old deep links', async () => {
  render(<App />);
  follow(/Report an issue/);
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'A fictional stuck window handle.' } });
  fireEvent.submit(input.closest('form')!);
  await screen.findByRole('heading', { name: 'Request #1049' });
  follow(/Your home/);
  await screen.findByRole('heading', { name: 'Your active tenancy' });
  fireEvent.click(screen.getByRole('button', { name: 'Reset demo' }));
  fireEvent.click(screen.getByRole('button', { name: 'Reset and start again' }));
  await screen.findByRole('heading', { name: 'Your active tenancy' });
  expect(screen.getByRole('region', { name: 'Maintenance summary' }).textContent).toContain('3 open requests');
  expect(screen.queryByRole('group', { name: 'Start fresh?' })).toBeNull();
  history.replaceState(null, '', '/app/demo/requests/1049');
  fireEvent.popState(window);
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect(screen.queryByRole('heading', { name: 'A fictional stuck window handle.' })).toBeNull();
  expect(fetchMock).not.toHaveBeenCalled();
});

it('restores the tour step on browser navigation and never labels an unknown page as a completed step', async () => {
  render(<App />);
  follow(/Follow a repair/);
  await screen.findByRole('heading', { name: 'Status timeline' });
  history.replaceState(null, '', '/app/demo/requests?page=1');
  fireEvent.popState(window);
  await screen.findByRole('heading', { name: /All requests/ });
  expect(within(guide()).getByRole('link', { name: /Browse requests/ }).getAttribute('aria-current')).toBe('step');
  history.replaceState(null, '', '/app/demo/missing');
  fireEvent.popState(window);
  await screen.findByRole('heading', { name: 'Page not found' });
  expect(guide().querySelector('[aria-current="step"]')).toBeNull();
  expect(fetchMock).not.toHaveBeenCalled();
});

it('never exposes demo controls on a live route or a similar route name', async () => {
  history.replaceState(null, '', '/app/demonstration');
  render(<App />);
  await screen.findByText('Sign in to access your RentRight account.');
  expect(screen.queryByRole('navigation', { name: 'Demo walkthrough' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Reset demo' })).toBeNull();
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/session', expect.anything());
});

it('can hide the walkthrough without losing simulated changes or making a network request', async () => {
  render(<App />);
  follow(/Report an issue/);
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'Fictional text stays while the guide is hidden.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Hide walkthrough' }));
  expect(screen.queryByRole('navigation', { name: 'Demo walkthrough' })).toBeNull();
  expect((input as HTMLTextAreaElement).value).toBe('Fictional text stays while the guide is hidden.');
  expect(fetchMock).not.toHaveBeenCalled();
});

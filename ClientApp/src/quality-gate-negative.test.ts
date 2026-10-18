// Temporary P11-01 gate probe; removed after observing the hosted failing run.
import { expect, it } from 'vitest';

it('rejects a deliberately failing PR test', () => {
  expect(false).toBe(true);
});

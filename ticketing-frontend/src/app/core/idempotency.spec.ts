import { idempotencyKeyFor } from './idempotency';

describe('idempotencyKeyFor', () => {
  it('returns the same key for the same hold until cleared', () => {
    const storage = new Map<string, string>();
    const fake = {
      getItem: (k: string) => storage.get(k) ?? null,
      setItem: (k: string, v: string) => storage.set(k, v),
    } as unknown as Storage;

    const first = idempotencyKeyFor('hold-1', fake);
    expect(idempotencyKeyFor('hold-1', fake)).toBe(first);
    expect(idempotencyKeyFor('hold-2', fake)).not.toBe(first);
  });
});

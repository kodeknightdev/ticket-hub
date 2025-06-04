/** One key per hold, kept across reloads so a refresh mid-checkout can't create a second order. */
export function idempotencyKeyFor(holdId: string, storage: Storage = sessionStorage): string {
  const name = `idem:${holdId}`;
  let key = storage.getItem(name);
  if (!key) {
    key = crypto.randomUUID();
    storage.setItem(name, key);
  }
  return key;
}

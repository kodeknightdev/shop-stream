import type { CartItem } from '../api/types';

const STORAGE_KEY = 'shop-stream.checkout-key';
const MAX_AGE_MS = 24 * 60 * 60 * 1000;

interface StoredKey {
  fingerprint: string;
  key: string;
  createdAt: number;
}

// fallback when sessionStorage is blocked
let memory: StoredKey | null = null;

export function cartFingerprint(userId: string, items: Pick<CartItem, 'productId' | 'quantity' | 'unitPrice'>[]): string {
  const lines = items
    .map((i) => `${i.productId}:${i.quantity}:${i.unitPrice}`)
    .sort()
    .join('|');
  return `${userId}#${lines}`;
}

function read(): StoredKey | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as StoredKey) : memory;
  } catch {
    return memory;
  }
}

function write(value: StoredKey | null) {
  memory = value;
  try {
    if (value) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    else sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // blocked, the memory copy is enough
  }
}

// Same user + same cart = same key, also after a reload, so retrying after a lost response
// gets the existing order back instead of a second one.
export function getCheckoutKey(userId: string, items: Pick<CartItem, 'productId' | 'quantity' | 'unitPrice'>[], now = Date.now()): string {
  const fingerprint = cartFingerprint(userId, items);
  const stored = read();
  if (stored && stored.fingerprint === fingerprint && now - stored.createdAt < MAX_AGE_MS) {
    return stored.key;
  }

  const fresh: StoredKey = { fingerprint, key: crypto.randomUUID(), createdAt: now };
  write(fresh);
  return fresh.key;
}

export function clearCheckoutKey() {
  write(null);
}

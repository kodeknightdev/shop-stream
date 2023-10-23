import { vi } from 'vitest';
import { cartFingerprint, clearCheckoutKey, getCheckoutKey } from '../lib/checkoutKey';

const cart = [
  { productId: 'p1', quantity: 2, unitPrice: 16.99 },
  { productId: 'p2', quantity: 1, unitPrice: 129.99 },
];

describe('checkout idempotency key', () => {
  it('is stable for the same user and cart, and survives a reload (sessionStorage)', () => {
    const first = getCheckoutKey('u1', cart);
    expect(getCheckoutKey('u1', [...cart].reverse())).toBe(first); // line order does not matter
    expect(JSON.parse(sessionStorage.getItem('shop-stream.checkout-key')!).key).toBe(first);
  });

  it('changes when the cart or the user changes', () => {
    const first = getCheckoutKey('u1', cart);
    expect(getCheckoutKey('u1', [{ ...cart[0], quantity: 3 }, cart[1]])).not.toBe(first);
    expect(getCheckoutKey('u2', cart)).not.toBe(getCheckoutKey('u1', cart));
  });

  it('is replaced after the order is accepted, or after 24 hours', () => {
    const first = getCheckoutKey('u1', cart, 1_000);
    clearCheckoutKey();
    expect(sessionStorage.getItem('shop-stream.checkout-key')).toBeNull();
    const second = getCheckoutKey('u1', cart, 2_000);
    expect(second).not.toBe(first);
    expect(getCheckoutKey('u1', cart, 2_000 + 24 * 60 * 60 * 1000)).not.toBe(second);
  });

  it('still works when sessionStorage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    try {
      const key = getCheckoutKey('u1', cart);
      expect(getCheckoutKey('u1', cart)).toBe(key);
    } finally {
      vi.restoreAllMocks();
    }
  });

  it('fingerprints user plus sorted lines', () => {
    expect(cartFingerprint('u1', cart)).toBe('u1#p1:2:16.99|p2:1:129.99');
  });
});

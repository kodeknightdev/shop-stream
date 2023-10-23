import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CheckoutPage } from '../pages/CheckoutPage';
import { mockFetch, renderWithProviders, signIn } from '../test/utils';

const cart = {
  userId: 'u1', itemCount: 2, subtotal: 33.98, hasStalePrices: false, updatedAt: null,
  items: [{ productId: 'p1', sku: 'B-1017', name: 'Night Garden', imageUrl: 'https://img', unitPrice: 16.99, quantity: 2, priceStale: false }],
};

async function fillAndSubmit() {
  await userEvent.type(await screen.findByLabelText('Address'), '1 Main St');
  await userEvent.type(screen.getByLabelText('City'), 'Springfield');
  await userEvent.type(screen.getByLabelText('Postal code'), '12345');
  await userEvent.click(screen.getByRole('button', { name: 'Place order' }));
}

beforeEach(() => signIn());

it('reuses the same Idempotency-Key after a lost response and a page reload, then forgets it', async () => {
  const keys: string[] = [];
  let attempt = 0;
  mockFetch((url, init) => {
    if (url === '/api/cart') return { body: cart };
    if (url === '/api/payments/provider') return { body: { provider: 'mock' } };
    if (url === '/api/orders' && init?.method === 'POST') {
      keys.push((init.headers as Record<string, string>)['Idempotency-Key']);
      attempt += 1;
      // the order may exist server-side, the client just sees a 502
      return attempt === 1 ? { status: 502, body: { title: 'Bad gateway', status: 502 } } : { status: 201, body: { id: 'o1', priceAdjustments: [] } };
    }
    return undefined;
  });

  const first = renderWithProviders(<CheckoutPage />, { route: '/checkout', path: '/checkout' });
  await fillAndSubmit();
  expect(await screen.findByRole('alert')).toBeInTheDocument();
  first.unmount(); // the customer reloads the page

  renderWithProviders(<CheckoutPage />, { route: '/checkout', path: '/checkout' });
  await fillAndSubmit();

  await waitFor(() => expect(keys).toHaveLength(2));
  expect(keys[1]).toBe(keys[0]);
  await waitFor(() => expect(sessionStorage.getItem('shop-stream.checkout-key')).toBeNull());
});

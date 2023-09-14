import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CartPage } from '../pages/CartPage';
import { mockFetch, renderWithProviders, signIn } from '../test/utils';

const item = { productId: 'p1', sku: 'BOO-1', name: 'Night Garden', imageUrl: 'https://img', unitPrice: 16.99, quantity: 2, priceStale: false };
const cart = (items = [item]) => ({
  userId: 'u1', items, itemCount: items.reduce((n, i) => n + i.quantity, 0),
  subtotal: items.reduce((s, i) => s + i.unitPrice * i.quantity, 0), hasStalePrices: items.some((i) => i.priceStale), updatedAt: null,
});

beforeEach(() => signIn());

it('shows lines, subtotal and a stale price warning', async () => {
  mockFetch((url) => (url === '/api/cart' ? { body: cart([{ ...item, priceStale: true, latestPrice: 14.99 } as typeof item]) } : undefined));
  renderWithProviders(<CartPage />);

  expect(await screen.findByText('Night Garden')).toBeInTheDocument();
  expect(screen.getByTestId('subtotal')).toHaveTextContent('$33.98');
  expect(screen.getByText(/Some prices changed/)).toBeInTheDocument();
  expect(screen.getByText('now $14.99')).toBeInTheDocument();
});

it('removes a line', async () => {
  const fetch = mockFetch((url, init) => {
    if (url === '/api/cart') return { body: cart() };
    if (url === '/api/cart/items/p1' && init?.method === 'DELETE') return { body: cart([]) };
    return undefined;
  });
  renderWithProviders(<CartPage />);

  await userEvent.click(await screen.findByRole('button', { name: 'Remove' }));

  await waitFor(() => expect(screen.getByText('Your cart is empty')).toBeInTheDocument());
  expect(fetch).toHaveBeenCalledWith('/api/cart/items/p1', expect.objectContaining({ method: 'DELETE' }));
});

it('sends the bearer token and redirects away when it cannot be refreshed', async () => {
  const fetch = mockFetch((url) => (url === '/api/auth/refresh' ? { status: 401, body: { title: 'expired' } } : { status: 401, body: { title: 'Unauthorized' } }));
  renderWithProviders(<CartPage />);

  expect(await screen.findByRole('alert')).toBeInTheDocument();
  const [, init] = fetch.mock.calls[0];
  expect((init!.headers as Record<string, string>).Authorization).toBe('Bearer access');
  expect(localStorage.getItem('shop-stream.auth')).toBeNull();
});

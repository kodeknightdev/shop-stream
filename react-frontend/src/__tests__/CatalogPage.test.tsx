import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CatalogPage } from '../pages/CatalogPage';
import { mockFetch, renderWithProviders } from '../test/utils';

const product = (id: string, name: string, price: number) => ({
  id, sku: `SKU-${id}`, name, slug: id, price, currency: 'USD', imageUrl: 'https://img', categoryId: 'c1', categoryName: 'Books', isActive: true,
});

it('lists products and sends search terms to the API', async () => {
  const fetch = mockFetch((url) => {
    if (url.startsWith('/api/categories')) return { body: [{ id: 'c1', name: 'Books', slug: 'books', productCount: 2 }] };
    if (url.startsWith('/api/products')) {
      const search = new URL(url, 'http://x').searchParams.get('search');
      const items = search ? [product('2', 'Event-Driven Design', 47)] : [product('1', 'Night Garden', 16.99), product('2', 'Event-Driven Design', 47)];
      return { body: { items, page: 1, pageSize: 12, total: items.length } };
    }
    return undefined;
  });

  renderWithProviders(<CatalogPage />);

  expect(await screen.findByText('Night Garden')).toBeInTheDocument();
  expect(screen.getByText('2 products')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /Books/ })).toBeInTheDocument();

  await userEvent.type(screen.getByRole('searchbox', { name: 'Search products' }), 'event');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));

  await waitFor(() => expect(screen.queryByText('Night Garden')).not.toBeInTheDocument());
  expect(fetch.mock.calls.some(([u]) => String(u).includes('search=event'))).toBe(true);
});

import { screen } from '@testing-library/react';
import { OrderDetailPage } from '../pages/OrderDetailPage';
import { mockFetch, renderWithProviders, signIn } from '../test/utils';

const at = '2023-09-01T10:00:00Z';
const order = {
  id: 'o1', number: 'SS-230901-000001', status: 'Confirmed', total: 33.98, currency: 'USD', email: 'jane@shopstream.local',
  paymentMethod: 'pm_card_visa', canCancel: false, createdAt: at, updatedAt: at,
  shippingAddress: { fullName: 'Jane Doe', line1: '42 Harbour Road', city: 'Portland', postalCode: '97201', country: 'US' },
  items: [{ productId: 'p1', sku: 'B-1017', name: 'Night Garden', quantity: 2, unitPrice: 16.99, lineTotal: 33.98 }],
  timeline: [
    { from: null, to: 'Pending', note: 'Order placed', occurredAt: at },
    { from: 'Pending', to: 'InventoryReserved', note: 'Stock reserved', occurredAt: at },
    { from: 'InventoryReserved', to: 'Paid', note: 'Payment captured', occurredAt: at },
    { from: 'Paid', to: 'Confirmed', note: 'Order confirmed', occurredAt: at },
  ],
};

beforeEach(() => signIn());

it('shows the composed view: payment and current product price', async () => {
  mockFetch((url) => (url === '/api/orders/o1/details' ? {
    body: {
      order,
      payment: { id: 'pay1', orderId: 'o1', amount: 33.98, currency: 'USD', provider: 'mock', status: 'Succeeded', createdAt: at },
      products: [{ productId: 'p1', orderedName: 'Night Garden', orderedUnitPrice: 16.99, quantity: 2, currentName: 'Night Garden', currentPrice: 14.99, stillAvailable: true }],
      warnings: [],
    },
  } : undefined));
  renderWithProviders(<OrderDetailPage />, { route: '/orders/o1', path: '/orders/:id' });

  expect(await screen.findByText('SS-230901-000001')).toBeInTheDocument();
  expect(screen.getByTestId('payment')).toHaveTextContent('Paid $33.98 via mock');
  expect(screen.getByTestId('price-now')).toHaveTextContent('now $14.99');
});

it('still renders the order when a dependency is down', async () => {
  mockFetch((url) => (url === '/api/orders/o1/details' ? {
    body: { order, payment: null, products: [{ productId: 'p1', orderedName: 'Night Garden', orderedUnitPrice: 16.99, quantity: 2 }], warnings: ['payments is unavailable; some details are missing.'] },
  } : undefined));
  renderWithProviders(<OrderDetailPage />, { route: '/orders/o1', path: '/orders/:id' });

  expect(await screen.findByText(/payments is unavailable/)).toBeInTheDocument();
  expect(screen.getByText('Payment details are unavailable right now.')).toBeInTheDocument();
});

import { render, screen } from '@testing-library/react';
import { OrderTimeline } from '../components/OrderTimeline';
import type { StatusChange } from '../api/types';

const at = '2023-09-01T10:00:00Z';

it('marks reached steps and leaves the rest pending', () => {
  const timeline: StatusChange[] = [
    { from: null, to: 'Pending', note: 'Order placed', occurredAt: at },
    { from: 'Pending', to: 'InventoryReserved', note: 'Stock reserved', occurredAt: at },
  ];
  render(<OrderTimeline timeline={timeline} status="InventoryReserved" />);

  expect(screen.getByTestId('step-Pending')).toHaveAttribute('data-done', 'true');
  expect(screen.getByTestId('step-InventoryReserved')).toHaveAttribute('data-done', 'true');
  expect(screen.getByTestId('step-Paid')).toHaveAttribute('data-done', 'false');
  expect(screen.getByText(/updates automatically/)).toBeInTheDocument();
});

it('shows the cancellation instead of the remaining happy path', () => {
  const timeline: StatusChange[] = [
    { from: null, to: 'Pending', note: 'Order placed', occurredAt: at },
    { from: 'Pending', to: 'Cancelled', note: 'Inventory: Insufficient stock', occurredAt: at },
  ];
  render(<OrderTimeline timeline={timeline} status="Cancelled" />);

  expect(screen.getByTestId('step-Cancelled')).toBeInTheDocument();
  expect(screen.queryByTestId('step-Confirmed')).not.toBeInTheDocument();
  expect(screen.getByText(/Insufficient stock/)).toBeInTheDocument();
});

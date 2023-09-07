import type { OrderStatus } from '../api/types';

const styles: Record<OrderStatus, string> = {
  Pending: 'bg-amber-100 text-amber-800',
  InventoryReserved: 'bg-sky-100 text-sky-800',
  Paid: 'bg-indigo-100 text-indigo-800',
  Confirmed: 'bg-emerald-100 text-emerald-800',
  Cancelled: 'bg-red-100 text-red-800',
};

export const statusLabel = (status: OrderStatus) => (status === 'InventoryReserved' ? 'Stock reserved' : status);

export function StatusBadge({ status }: { status: OrderStatus }) {
  return <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${styles[status]}`}>{statusLabel(status)}</span>;
}

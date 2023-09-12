import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/endpoints';
import { ErrorMessage, Spinner } from '../../components/ErrorMessage';
import { Pagination } from '../../components/Pagination';
import { StatusBadge } from '../../components/StatusBadge';
import { formatDateTime, formatMoney } from '../../lib/format';

const STATUSES = ['', 'Pending', 'InventoryReserved', 'Paid', 'Confirmed', 'Cancelled'];

export function AdminOrdersPage() {
  const [filters, setFilters] = useState({ status: '', email: '', from: '', to: '', page: 1 });
  const orders = useQuery({
    queryKey: ['admin-orders', filters],
    queryFn: () => adminApi.orders({
      ...filters,
      from: filters.from ? new Date(filters.from).toISOString() : undefined,
      to: filters.to ? new Date(`${filters.to}T23:59:59`).toISOString() : undefined,
    }),
    keepPreviousData: true,
    refetchInterval: 5000,
  });
  const set = (changes: Partial<typeof filters>) => setFilters({ ...filters, page: 1, ...changes });

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-3">
        <select aria-label="Status filter" className="input w-auto" value={filters.status} onChange={(e) => set({ status: e.target.value })}>
          {STATUSES.map((s) => <option key={s} value={s}>{s || 'All statuses'}</option>)}
        </select>
        <input aria-label="Customer email" className="input w-56" placeholder="Customer email" value={filters.email} onChange={(e) => set({ email: e.target.value })} />
        <input aria-label="From date" type="date" className="input w-auto" value={filters.from} onChange={(e) => set({ from: e.target.value })} />
        <input aria-label="To date" type="date" className="input w-auto" value={filters.to} onChange={(e) => set({ to: e.target.value })} />
      </div>
      {orders.isLoading && <Spinner />}
      <ErrorMessage error={orders.error} />
      {orders.data && (
        <>
          <table className="card w-full text-sm">
            <thead className="bg-slate-50 text-left text-slate-500">
              <tr><th className="p-3">Order</th><th className="p-3">Customer</th><th className="p-3">Placed</th><th className="p-3">Status</th><th className="p-3 text-right">Total</th></tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {orders.data.items.map((o) => (
                <tr key={o.id}>
                  <td className="p-3"><Link className="font-mono text-brand-700 hover:underline" to={`/orders/${o.id}`}>{o.number}</Link></td>
                  <td className="p-3">{o.email}</td>
                  <td className="p-3">{formatDateTime(o.createdAt)}</td>
                  <td className="p-3"><StatusBadge status={o.status} /></td>
                  <td className="p-3 text-right">{formatMoney(o.total, o.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination page={orders.data.page} pageSize={orders.data.pageSize} total={orders.data.total} onChange={(page) => setFilters({ ...filters, page })} />
        </>
      )}
    </div>
  );
}

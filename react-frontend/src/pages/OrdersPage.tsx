import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ordersApi } from '../api/endpoints';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';
import { Pagination } from '../components/Pagination';
import { StatusBadge } from '../components/StatusBadge';
import { formatDateTime, formatMoney } from '../lib/format';

export function OrdersPage() {
  const [page, setPage] = useState(1);
  const orders = useQuery({ queryKey: ['orders', page], queryFn: () => ordersApi.mine(page), keepPreviousData: true });

  if (orders.isLoading) return <Spinner />;
  if (orders.error) return <ErrorMessage error={orders.error} />;
  const data = orders.data!;

  return (
    <div>
      <h1 className="mb-4 text-2xl font-bold">Your orders</h1>
      {data.items.length === 0 ? (
        <p className="text-slate-500">No orders yet. <Link to="/" className="text-brand-700 hover:underline">Start shopping</Link></p>
      ) : (
        <table className="card w-full overflow-hidden text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr><th className="p-3">Order</th><th className="p-3">Placed</th><th className="p-3">Items</th><th className="p-3">Status</th><th className="p-3 text-right">Total</th></tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.items.map((o) => (
              <tr key={o.id} className="hover:bg-slate-50">
                <td className="p-3"><Link to={`/orders/${o.id}`} className="font-mono text-brand-700 hover:underline">{o.number}</Link></td>
                <td className="p-3">{formatDateTime(o.createdAt)}</td>
                <td className="p-3">{o.itemCount}</td>
                <td className="p-3"><StatusBadge status={o.status} /></td>
                <td className="p-3 text-right font-medium">{formatMoney(o.total, o.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onChange={setPage} />
    </div>
  );
}

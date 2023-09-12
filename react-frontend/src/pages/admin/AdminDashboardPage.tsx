import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../../api/endpoints';
import { ErrorMessage, Spinner } from '../../components/ErrorMessage';
import { formatDay, formatMoney } from '../../lib/format';
import { SalesChart } from './SalesChart';

export function AdminDashboardPage() {
  const [days, setDays] = useState(14);
  const summary = useQuery({ queryKey: ['sales-summary', days], queryFn: () => adminApi.salesSummary(days), refetchInterval: 15000 });

  if (summary.isLoading) return <Spinner />;
  if (summary.error) return <ErrorMessage error={summary.error} />;
  const s = summary.data!;
  const tiles = [
    { label: 'Revenue', value: formatMoney(s.revenue) },
    { label: 'Orders', value: s.totalOrders },
    { label: 'Confirmed', value: s.confirmedOrders },
    { label: 'Avg. order value', value: formatMoney(s.averageOrderValue) },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <h2 className="text-lg font-semibold">Sales</h2>
        <select aria-label="Window" className="input w-auto" value={days} onChange={(e) => setDays(Number(e.target.value))}>
          {[7, 14, 30, 90].map((d) => <option key={d} value={d}>Last {d} days</option>)}
        </select>
      </div>
      <div className="grid gap-4 sm:grid-cols-4">
        {tiles.map((t) => (
          <div key={t.label} className="card p-4">
            <p className="text-xs uppercase tracking-wide text-slate-500">{t.label}</p>
            <p className="mt-1 text-2xl font-semibold">{t.value}</p>
          </div>
        ))}
      </div>
      <div className="card p-4"><SalesChart days={s.daily} /></div>
      <table className="card w-full text-sm">
        <thead className="bg-slate-50 text-left text-slate-500">
          <tr><th className="p-2">Day</th><th className="p-2">Orders</th><th className="p-2">Confirmed</th><th className="p-2">Cancelled</th><th className="p-2 text-right">Revenue</th></tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {[...s.daily].reverse().map((d) => (
            <tr key={d.date}>
              <td className="p-2">{formatDay(d.date)}</td><td className="p-2">{d.orders}</td><td className="p-2">{d.confirmedOrders}</td>
              <td className="p-2">{d.cancelledOrders}</td><td className="p-2 text-right">{formatMoney(d.revenue)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

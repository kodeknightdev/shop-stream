import { Link, useLocation, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi } from '../api/endpoints';
import type { PriceAdjustment } from '../api/types';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';
import { OrderTimeline } from '../components/OrderTimeline';
import { StatusBadge } from '../components/StatusBadge';
import { formatDateTime, formatMoney } from '../lib/format';

export function OrderDetailPage() {
  const { id = '' } = useParams();
  const location = useLocation();
  const state = location.state as { justPlaced?: boolean; adjustments?: PriceAdjustment[] } | null;
  const queryClient = useQueryClient();

  const details = useQuery({
    queryKey: ['order', id],
    queryFn: () => ordersApi.details(id),
    // poll while the saga is still running
    refetchInterval: (data) => (data && (data.order.status === 'Confirmed' || data.order.status === 'Cancelled') ? false : 1500),
  });
  const cancel = useMutation({ mutationFn: () => ordersApi.cancel(id), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['order', id] }) });

  if (details.isLoading) return <Spinner />;
  if (details.error) return <ErrorMessage error={details.error} />;
  const { order: o, payment, products, warnings } = details.data!;
  const current = new Map(products.map((p) => [p.productId, p]));

  return (
    <div className="space-y-6">
      <Link to="/orders" className="text-sm text-brand-700 hover:underline">&larr; All orders</Link>
      {state?.justPlaced && <div className="rounded-md bg-emerald-50 p-3 text-sm text-emerald-800">Thanks! Your order was placed. We'll email you as it progresses.</div>}
      {state?.adjustments && state.adjustments.length > 0 && (
        <div className="rounded-md bg-amber-50 p-3 text-sm text-amber-800">
          Prices were updated at checkout: {state.adjustments.map((a) => `${a.name} ${formatMoney(a.cartPrice)} → ${formatMoney(a.catalogPrice)}`).join(', ')}
        </div>
      )}
      <div className="flex flex-wrap items-center gap-4">
        <h1 className="font-mono text-2xl font-bold">{o.number}</h1>
        <StatusBadge status={o.status} />
        <span className="text-sm text-slate-500">Placed {formatDateTime(o.createdAt)}</span>
        {o.canCancel && (
          <button className="btn-danger ml-auto" disabled={cancel.isLoading} onClick={() => cancel.mutate()}>Cancel order</button>
        )}
      </div>
      <ErrorMessage error={cancel.error} />
      {warnings.length > 0 && (
        <div role="status" className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">{warnings.join(' ')}</div>
      )}
      {o.cancellationReason && <p className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">{o.cancellationReason}</p>}

      <div className="grid gap-6 lg:grid-cols-[1fr_300px]">
        <section className="card p-5">
          <h2 className="mb-3 font-semibold">Items</h2>
          <ul className="divide-y divide-slate-100 text-sm">
            {o.items.map((i) => {
              const now = current.get(i.productId);
              return (
                <li key={i.productId} className="flex justify-between gap-4 py-2">
                  <span>
                    {i.quantity} x {i.name}
                    {now?.currentPrice != null && now.currentPrice !== i.unitPrice && (
                      <span className="ml-2 text-xs text-slate-500" data-testid="price-now">now {formatMoney(now.currentPrice, o.currency)}</span>
                    )}
                    {now?.stillAvailable === false && <span className="ml-2 text-xs text-red-600">no longer sold</span>}
                  </span>
                  <span>{formatMoney(i.lineTotal, o.currency)}</span>
                </li>
              );
            })}
          </ul>
          <div className="mt-3 flex justify-between border-t pt-3 font-semibold"><span>Total</span><span>{formatMoney(o.total, o.currency)}</span></div>
          <h2 className="mb-1 mt-6 font-semibold">Payment</h2>
          {payment ? (
            <p className="text-sm text-slate-600" data-testid="payment">
              {payment.status === 'Succeeded' ? 'Paid' : 'Failed'} {formatMoney(payment.amount, payment.currency)} via {payment.provider}
              {payment.failureReason && <> - {payment.failureReason}</>}
            </p>
          ) : (
            <p className="text-sm text-slate-500">{warnings.length ? 'Payment details are unavailable right now.' : 'Not charged yet.'}</p>
          )}
          <h2 className="mb-1 mt-6 font-semibold">Ship to</h2>
          <p className="text-sm text-slate-600">
            {o.shippingAddress.fullName}<br />{o.shippingAddress.line1}{o.shippingAddress.line2 && <>, {o.shippingAddress.line2}</>}<br />
            {o.shippingAddress.postalCode} {o.shippingAddress.city}, {o.shippingAddress.country}
          </p>
        </section>
        <section className="card p-5">
          <h2 className="mb-4 font-semibold">Status</h2>
          <OrderTimeline timeline={o.timeline} status={o.status} />
        </section>
      </div>
    </div>
  );
}

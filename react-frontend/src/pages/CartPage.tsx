import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { cartApi } from '../api/endpoints';
import type { Cart } from '../api/types';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';
import { formatMoney } from '../lib/format';

export function CartPage() {
  const queryClient = useQueryClient();
  const cart = useQuery({ queryKey: ['cart'], queryFn: cartApi.get });
  const onCart = (next: Cart) => queryClient.setQueryData(['cart'], next);

  const update = useMutation({ mutationFn: (v: { productId: string; quantity: number }) => cartApi.update(v.productId, v.quantity), onSuccess: onCart });
  const remove = useMutation({ mutationFn: cartApi.remove, onSuccess: onCart });
  const refresh = useMutation({ mutationFn: cartApi.validate, onSuccess: (r) => onCart(r.cart) });

  if (cart.isLoading) return <Spinner />;
  if (cart.error) return <ErrorMessage error={cart.error} />;
  const data = cart.data!;

  if (data.items.length === 0) {
    return (
      <div className="py-16 text-center">
        <h1 className="mb-2 text-2xl font-bold">Your cart is empty</h1>
        <Link to="/" className="text-brand-700 hover:underline">Browse the catalog</Link>
      </div>
    );
  }

  return (
    <div className="grid gap-8 lg:grid-cols-[1fr_320px]">
      <section>
        <h1 className="mb-4 text-2xl font-bold">Cart</h1>
        {data.hasStalePrices && (
          <div className="mb-4 flex items-center justify-between rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <span>Some prices changed since you added them.</span>
            <button className="btn-secondary" onClick={() => refresh.mutate()} disabled={refresh.isLoading}>Update prices</button>
          </div>
        )}
        <ErrorMessage error={update.error ?? remove.error ?? refresh.error} />
        <ul className="divide-y divide-slate-200 card">
          {data.items.map((item) => (
            <li key={item.productId} className="flex items-center gap-4 p-4">
              <img src={item.imageUrl} alt="" className="h-16 w-16 rounded object-cover" />
              <div className="flex-1">
                <Link to={`/products/${item.productId}`} className="font-medium hover:underline">{item.name}</Link>
                <p className="text-sm text-slate-500">
                  {formatMoney(item.unitPrice)}
                  {item.priceStale && item.latestPrice !== undefined && (
                    <span className="ml-2 text-amber-700">now {formatMoney(item.latestPrice)}</span>
                  )}
                </p>
              </div>
              <input aria-label={`Quantity for ${item.name}`} type="number" min={0} max={20} className="input w-20" defaultValue={item.quantity}
                onBlur={(e) => {
                  const quantity = Math.min(20, Math.max(0, Number(e.target.value) || 0));
                  if (quantity !== item.quantity) update.mutate({ productId: item.productId, quantity });
                }} />
              <span className="w-24 text-right font-medium">{formatMoney(item.unitPrice * item.quantity)}</span>
              <button className="text-sm text-red-600 hover:underline" onClick={() => remove.mutate(item.productId)}>Remove</button>
            </li>
          ))}
        </ul>
      </section>
      <aside className="card h-fit space-y-4 p-5">
        <div className="flex justify-between text-sm"><span>Items</span><span>{data.itemCount}</span></div>
        <div className="flex justify-between text-lg font-semibold"><span>Subtotal</span><span data-testid="subtotal">{formatMoney(data.subtotal)}</span></div>
        <p className="text-xs text-slate-500">Final prices are confirmed against the catalog at checkout.</p>
        <Link to="/checkout" className="btn-primary w-full">Checkout</Link>
      </aside>
    </div>
  );
}

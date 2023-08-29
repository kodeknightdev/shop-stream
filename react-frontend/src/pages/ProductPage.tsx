import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { cartApi, catalogApi, inventoryApi } from '../api/endpoints';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';
import { formatMoney } from '../lib/format';

export function ProductPage() {
  const { id = '' } = useParams();
  const { user } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [quantity, setQuantity] = useState(1);

  const product = useQuery({ queryKey: ['product', id], queryFn: () => catalogApi.product(id) });
  const stock = useQuery({ queryKey: ['stock', id], queryFn: () => inventoryApi.stock(id), retry: false });
  const add = useMutation({
    mutationFn: () => cartApi.add(id, quantity),
    onSuccess: (cart) => queryClient.setQueryData(['cart'], cart),
  });

  if (product.isLoading) return <Spinner />;
  if (product.error || !product.data) return <ErrorMessage error={product.error ?? new Error('Product not found')} />;
  const p = product.data;
  const available = stock.data?.available ?? 0;
  const soldOut = stock.isSuccess && !stock.data.inStock;

  return (
    <div className="grid gap-10 md:grid-cols-2">
      <img src={p.imageUrl} alt={p.name} className="card aspect-[4/3] w-full object-cover" />
      <div className="space-y-5">
        <Link to={`/?category=${p.categorySlug}`} className="text-sm text-brand-700 hover:underline">{p.categoryName}</Link>
        <h1 className="text-3xl font-bold">{p.name}</h1>
        <p className="text-2xl font-semibold text-brand-700">{formatMoney(p.price, p.currency)}</p>
        <p className="text-slate-600">{p.description}</p>

        <div className="text-sm" data-testid="stock">
          {stock.isLoading && <span className="text-slate-500">Checking stock...</span>}
          {stock.isSuccess && (soldOut
            ? <span className="font-medium text-red-600">Out of stock</span>
            : stock.data.lowStock
              ? <span className="font-medium text-amber-600">Only {available} left</span>
              : <span className="font-medium text-emerald-600">In stock</span>)}
        </div>

        {!p.isActive ? (
          <p className="text-slate-500">This product is no longer sold.</p>
        ) : (
          <div className="flex items-center gap-3">
            <label className="sr-only" htmlFor="qty">Quantity</label>
            <input id="qty" type="number" min={1} max={20} value={quantity} className="input w-20"
              onChange={(e) => setQuantity(Math.min(20, Math.max(1, Number(e.target.value) || 1)))} />
            <button className="btn-primary" disabled={soldOut || add.isLoading}
              onClick={() => (user ? add.mutate() : navigate('/login', { state: { from: `/products/${id}` } }))}>
              {add.isLoading ? 'Adding...' : 'Add to cart'}
            </button>
            {add.isSuccess && <Link to="/cart" className="text-sm text-brand-700 hover:underline">Added - view cart</Link>}
          </div>
        )}
        <ErrorMessage error={add.error} />

        {Object.keys(p.attributes).length > 0 && (
          <dl className="card grid grid-cols-2 gap-x-4 gap-y-2 p-4 text-sm">
            {Object.entries(p.attributes).map(([k, v]) => (
              <div key={k} className="contents">
                <dt className="capitalize text-slate-500">{k}</dt>
                <dd className="text-slate-900">{v}</dd>
              </div>
            ))}
            <dt className="text-slate-500">SKU</dt>
            <dd className="font-mono text-slate-900">{p.sku}</dd>
          </dl>
        )}
      </div>
    </div>
  );
}

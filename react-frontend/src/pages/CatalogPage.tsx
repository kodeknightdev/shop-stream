import { FormEvent, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { catalogApi } from '../api/endpoints';
import { ProductCard } from '../components/ProductCard';
import { Pagination } from '../components/Pagination';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';

const SORTS = [
  { value: 'name', label: 'Name A-Z' },
  { value: 'price_asc', label: 'Price: low to high' },
  { value: 'price_desc', label: 'Price: high to low' },
  { value: 'newest', label: 'Newest' },
];

const num = (v: string | null) => (v ? Number(v) : undefined);

export function CatalogPage() {
  const [params, setParams] = useSearchParams();
  const filters = {
    search: params.get('search') ?? undefined,
    category: params.get('category') ?? undefined,
    minPrice: num(params.get('minPrice')),
    maxPrice: num(params.get('maxPrice')),
    sort: params.get('sort') ?? 'name',
    page: num(params.get('page')) ?? 1,
    pageSize: 12,
  };
  const [search, setSearch] = useState(filters.search ?? '');

  const categories = useQuery({ queryKey: ['categories'], queryFn: catalogApi.categories });
  const products = useQuery({ queryKey: ['products', filters], queryFn: () => catalogApi.products(filters), keepPreviousData: true });

  const update = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in changes)) next.delete('page');
    setParams(next);
  };

  const onSearch = (e: FormEvent) => {
    e.preventDefault();
    update({ search: search.trim() || undefined });
  };

  return (
    <div className="grid gap-8 md:grid-cols-[220px_1fr]">
      <aside className="space-y-6">
        <div>
          <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">Categories</h2>
          <ul className="space-y-1 text-sm">
            <li>
              <button className={!filters.category ? 'font-semibold text-brand-700' : 'text-slate-700'} onClick={() => update({ category: undefined })}>
                All products
              </button>
            </li>
            {categories.data?.map((c) => (
              <li key={c.id}>
                <button className={filters.category === c.slug ? 'font-semibold text-brand-700' : 'text-slate-700'} onClick={() => update({ category: c.slug })}>
                  {c.name} <span className="text-slate-400">({c.productCount})</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
        <div>
          <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">Price</h2>
          <div className="flex gap-2">
            <input aria-label="Minimum price" className="input" type="number" min={0} placeholder="Min" defaultValue={filters.minPrice}
              onBlur={(e) => update({ minPrice: e.target.value || undefined })} />
            <input aria-label="Maximum price" className="input" type="number" min={0} placeholder="Max" defaultValue={filters.maxPrice}
              onBlur={(e) => update({ maxPrice: e.target.value || undefined })} />
          </div>
        </div>
      </aside>

      <section>
        <div className="mb-6 flex flex-wrap items-center gap-3">
          <form onSubmit={onSearch} className="flex flex-1 gap-2">
            <input className="input" type="search" placeholder="Search products" aria-label="Search products" value={search}
              onChange={(e) => setSearch(e.target.value)} />
            <button className="btn-primary" type="submit">Search</button>
          </form>
          <select aria-label="Sort" className="input w-auto" value={filters.sort} onChange={(e) => update({ sort: e.target.value })}>
            {SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </div>

        {products.isLoading && <Spinner />}
        <ErrorMessage error={products.error} />
        {products.data && (
          <>
            <p className="mb-4 text-sm text-slate-500">{products.data.total} products</p>
            {products.data.items.length === 0 ? (
              <p className="py-12 text-center text-slate-500">No products match those filters.</p>
            ) : (
              <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
                {products.data.items.map((p) => <ProductCard key={p.id} product={p} />)}
              </div>
            )}
            <Pagination page={products.data.page} pageSize={products.data.pageSize} total={products.data.total}
              onChange={(page) => update({ page: String(page) })} />
          </>
        )}
      </section>
    </div>
  );
}

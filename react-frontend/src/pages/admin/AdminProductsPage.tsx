import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { catalogApi, inventoryApi, ProductInput } from '../../api/endpoints';
import type { ProductDetail } from '../../api/types';
import { ErrorMessage, Spinner } from '../../components/ErrorMessage';
import { Pagination } from '../../components/Pagination';
import { formatMoney } from '../../lib/format';
import { ProductForm } from './ProductForm';

export function AdminProductsPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<ProductDetail | 'new' | null>(null);

  const categories = useQuery({ queryKey: ['categories'], queryFn: catalogApi.categories });
  const products = useQuery({
    queryKey: ['admin-products', page, search],
    queryFn: () => catalogApi.products({ page, pageSize: 20, search, includeInactive: true, sort: 'name' }),
    keepPreviousData: true,
  });

  const done = () => {
    setEditing(null);
    queryClient.invalidateQueries({ queryKey: ['admin-products'] });
    queryClient.invalidateQueries({ queryKey: ['products'] });
  };

  const save = useMutation({
    mutationFn: async (input: ProductInput) => {
      if (editing && editing !== 'new') return catalogApi.updateProduct(editing.id, input);
      const created = await catalogApi.createProduct(input);
      // create the stock row so it can be restocked later
      await inventoryApi.adjust(created.id, 1, 'Initial stock record', created.sku).catch(() => undefined);
      return created;
    },
    onSuccess: done,
  });
  const deactivate = useMutation({ mutationFn: catalogApi.deactivateProduct, onSuccess: done });

  const openEdit = async (id: string) => setEditing(await catalogApi.product(id));

  return (
    <div className="space-y-4">
      <div className="flex gap-3">
        <input aria-label="Search products" className="input max-w-xs" placeholder="Search" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        <button className="btn-primary ml-auto" onClick={() => { save.reset(); setEditing('new'); }}>New product</button>
      </div>
      {editing && categories.data && (
        <ProductForm
          key={editing === 'new' ? 'new' : editing.id}
          categories={categories.data}
          initial={editing === 'new' ? undefined : editing}
          busy={save.isLoading}
          error={save.error}
          onSubmit={(input) => save.mutate(input)}
          onCancel={() => setEditing(null)}
        />
      )}
      {products.isLoading && <Spinner />}
      <ErrorMessage error={products.error ?? deactivate.error} />
      {products.data && (
        <>
          <table className="card w-full text-sm">
            <thead className="bg-slate-50 text-left text-slate-500">
              <tr><th className="p-3">Product</th><th className="p-3">SKU</th><th className="p-3">Category</th><th className="p-3 text-right">Price</th><th className="p-3">Status</th><th className="p-3" /></tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {products.data.items.map((p) => (
                <tr key={p.id} className={p.isActive ? '' : 'text-slate-400'}>
                  <td className="p-3">{p.name}</td>
                  <td className="p-3 font-mono">{p.sku}</td>
                  <td className="p-3">{p.categoryName}</td>
                  <td className="p-3 text-right">{formatMoney(p.price)}</td>
                  <td className="p-3">{p.isActive ? 'Active' : 'Inactive'}</td>
                  <td className="space-x-3 p-3 text-right">
                    <button className="text-brand-700 hover:underline" onClick={() => openEdit(p.id)}>Edit</button>
                    {p.isActive && <button className="text-red-600 hover:underline" onClick={() => deactivate.mutate(p.id)}>Deactivate</button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination page={products.data.page} pageSize={products.data.pageSize} total={products.data.total} onChange={setPage} />
        </>
      )}
    </div>
  );
}

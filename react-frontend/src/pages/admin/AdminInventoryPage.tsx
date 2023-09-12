import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { inventoryApi } from '../../api/endpoints';
import type { StockItemAdmin } from '../../api/types';
import { ErrorMessage, Spinner } from '../../components/ErrorMessage';

function AdjustRow({ item }: { item: StockItemAdmin }) {
  const queryClient = useQueryClient();
  const [delta, setDelta] = useState('');
  const adjust = useMutation({
    mutationFn: () => inventoryApi.adjust(item.productId, Number(delta), Number(delta) > 0 ? 'Restock' : 'Stock correction'),
    onSuccess: () => {
      setDelta('');
      queryClient.invalidateQueries({ queryKey: ['inventory'] });
    },
  });

  return (
    <tr>
      <td className="p-3 font-mono">{item.sku}</td>
      <td className="p-3">{item.onHand}</td>
      <td className="p-3">{item.reserved}</td>
      <td className="p-3 font-medium">{item.available}</td>
      <td className="p-3">{item.lowStock ? <span className="rounded bg-amber-100 px-2 py-0.5 text-xs text-amber-800">Low</span> : 'OK'}</td>
      <td className="p-3">
        <div className="flex items-center gap-2">
          <input aria-label={`Adjust ${item.sku}`} type="number" className="input w-24" placeholder="+/-" value={delta} onChange={(e) => setDelta(e.target.value)} />
          <button className="btn-secondary" disabled={!delta || Number(delta) === 0 || adjust.isLoading} onClick={() => adjust.mutate()}>Apply</button>
        </div>
        {adjust.error ? <div className="mt-1"><ErrorMessage error={adjust.error} /></div> : null}
      </td>
    </tr>
  );
}

export function AdminInventoryPage() {
  const [lowOnly, setLowOnly] = useState(false);
  const stock = useQuery({ queryKey: ['inventory', lowOnly], queryFn: () => inventoryApi.list(lowOnly), refetchInterval: 10000 });

  return (
    <div className="space-y-4">
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={lowOnly} onChange={(e) => setLowOnly(e.target.checked)} /> Show low stock only
      </label>
      {stock.isLoading && <Spinner />}
      <ErrorMessage error={stock.error} />
      {stock.data && (
        <table className="card w-full text-sm">
          <thead className="bg-slate-50 text-left text-slate-500">
            <tr><th className="p-3">SKU</th><th className="p-3">On hand</th><th className="p-3">Reserved</th><th className="p-3">Available</th><th className="p-3">Level</th><th className="p-3">Adjust</th></tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {stock.data.items.map((i) => <AdjustRow key={i.productId} item={i} />)}
          </tbody>
        </table>
      )}
    </div>
  );
}

import { FormEvent, useState } from 'react';
import type { Category, ProductDetail } from '../../api/types';
import type { ProductInput } from '../../api/endpoints';
import { ErrorMessage } from '../../components/ErrorMessage';

interface Props {
  categories: Category[];
  initial?: ProductDetail;
  busy?: boolean;
  error?: unknown;
  onSubmit: (input: ProductInput) => void;
  onCancel: () => void;
}

const toText = (attrs: Record<string, string>) => Object.entries(attrs).map(([k, v]) => `${k}=${v}`).join('\n');
const fromText = (text: string) =>
  Object.fromEntries(text.split('\n').map((l) => l.split('=')).filter((p) => p.length === 2 && p[0].trim()).map(([k, v]) => [k.trim(), v.trim()]));

export function ProductForm({ categories, initial, busy, error, onSubmit, onCancel }: Props) {
  const [form, setForm] = useState({
    categoryId: initial?.categoryId ?? categories[0]?.id ?? '',
    sku: initial?.sku ?? '',
    name: initial?.name ?? '',
    description: initial?.description ?? '',
    price: initial?.price.toString() ?? '',
    imageUrl: initial?.imageUrl ?? '',
    attributes: toText(initial?.attributes ?? {}),
    isActive: initial?.isActive ?? true,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    onSubmit({ ...form, price: Number(form.price), attributes: fromText(form.attributes) });
  };
  const text = (key: 'sku' | 'name' | 'price' | 'imageUrl', label: string, extra: Record<string, unknown> = {}) => (
    <div>
      <label className="label" htmlFor={`pf-${key}`}>{label}</label>
      <input id={`pf-${key}`} className="input" value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} {...extra} />
    </div>
  );

  return (
    <form onSubmit={submit} className="card grid gap-4 p-5 sm:grid-cols-2">
      <h2 className="font-semibold sm:col-span-2">{initial ? `Edit ${initial.name}` : 'New product'}</h2>
      {text('name', 'Name', { required: true })}
      {text('sku', 'SKU', { required: true, disabled: !!initial, pattern: '[A-Z0-9-]+' })}
      {text('price', 'Price (USD)', { required: true, type: 'number', step: '0.01', min: '0.01' })}
      <div>
        <label className="label" htmlFor="pf-category">Category</label>
        <select id="pf-category" className="input" value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </div>
      <div className="sm:col-span-2">{text('imageUrl', 'Image URL', { required: true, type: 'url' })}</div>
      <div className="sm:col-span-2">
        <label className="label" htmlFor="pf-description">Description</label>
        <textarea id="pf-description" rows={3} className="input" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </div>
      <div className="sm:col-span-2">
        <label className="label" htmlFor="pf-attributes">Attributes (one key=value per line)</label>
        <textarea id="pf-attributes" rows={3} className="input font-mono" value={form.attributes} onChange={(e) => setForm({ ...form, attributes: e.target.value })} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} /> Active (visible in the shop)
      </label>
      <div className="sm:col-span-2"><ErrorMessage error={error} /></div>
      <div className="flex gap-2 sm:col-span-2">
        <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Saving...' : 'Save'}</button>
        <button type="button" className="btn-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

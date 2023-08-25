interface Props {
  page: number;
  pageSize: number;
  total: number;
  onChange: (page: number) => void;
}

export function Pagination({ page, pageSize, total, onChange }: Props) {
  const pages = Math.max(1, Math.ceil(total / Math.max(pageSize, 1)));
  if (pages <= 1) return null;
  return (
    <nav className="mt-6 flex items-center justify-center gap-2" aria-label="Pagination">
      <button className="btn-secondary" disabled={page <= 1} onClick={() => onChange(page - 1)}>Previous</button>
      <span className="text-sm text-slate-600">Page {page} of {pages}</span>
      <button className="btn-secondary" disabled={page >= pages} onClick={() => onChange(page + 1)}>Next</button>
    </nav>
  );
}

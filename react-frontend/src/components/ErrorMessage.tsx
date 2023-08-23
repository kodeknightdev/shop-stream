import { ApiError } from '../api/client';

export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) return null;
  const lines = error instanceof ApiError ? error.messages : [error instanceof Error ? error.message : 'Something went wrong'];
  return (
    <div role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
      {lines.map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  );
}

export function Spinner({ label = 'Loading...' }: { label?: string }) {
  return (
    <div className="flex items-center gap-2 py-8 text-sm text-slate-500" role="status">
      <span className="h-4 w-4 animate-spin rounded-full border-2 border-brand-600 border-t-transparent" />
      {label}
    </div>
  );
}

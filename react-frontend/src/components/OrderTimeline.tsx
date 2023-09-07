import type { OrderStatus, StatusChange } from '../api/types';
import { formatDateTime } from '../lib/format';
import { statusLabel } from './StatusBadge';

const HAPPY_PATH: OrderStatus[] = ['Pending', 'InventoryReserved', 'Paid', 'Confirmed'];

export function OrderTimeline({ timeline, status }: { timeline: StatusChange[]; status: OrderStatus }) {
  const reached = new Map(timeline.map((t) => [t.to, t]));
  const cancelled = reached.get('Cancelled');
  const steps = cancelled ? [...HAPPY_PATH.filter((s) => reached.has(s)), 'Cancelled' as OrderStatus] : HAPPY_PATH;

  return (
    <ol className="relative space-y-4 border-l border-slate-200 pl-6" aria-label="Order timeline">
      {steps.map((step) => {
        const entry = reached.get(step);
        const done = !!entry;
        const isCancel = step === 'Cancelled';
        return (
          <li key={step} data-testid={`step-${step}`} data-done={done}>
            <span
              className={`absolute -left-[7px] mt-1 h-3 w-3 rounded-full ${
                isCancel ? 'bg-red-500' : done ? 'bg-brand-600' : 'bg-slate-300'
              }`}
            />
            <p className={`text-sm font-medium ${done ? 'text-slate-900' : 'text-slate-400'}`}>{statusLabel(step)}</p>
            {entry && (
              <p className="text-xs text-slate-500">
                {formatDateTime(entry.occurredAt)} · {entry.note}
              </p>
            )}
          </li>
        );
      })}
      {!cancelled && status !== 'Confirmed' && (
        <li className="text-xs text-slate-500">Processing - this page updates automatically.</li>
      )}
    </ol>
  );
}

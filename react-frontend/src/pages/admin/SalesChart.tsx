import type { SalesDay } from '../../api/types';
import { formatDay, formatMoney } from '../../lib/format';

export function SalesChart({ days }: { days: SalesDay[] }) {
  const max = Math.max(1, ...days.map((d) => d.revenue));
  const width = 640;
  const height = 200;
  const barWidth = width / Math.max(days.length, 1);

  return (
    <svg viewBox={`0 0 ${width} ${height + 24}`} className="w-full" role="img" aria-label="Revenue per day">
      {days.map((d, i) => {
        const h = (d.revenue / max) * height;
        return (
          <g key={d.date}>
            <rect x={i * barWidth + 4} y={height - h} width={barWidth - 8} height={Math.max(h, 1)} rx={3} className="fill-brand-600">
              <title>{`${formatDay(d.date)}: ${formatMoney(d.revenue)} from ${d.confirmedOrders} confirmed / ${d.orders} orders`}</title>
            </rect>
            {(days.length <= 14 || i % 2 === 0) && (
              <text x={i * barWidth + barWidth / 2} y={height + 16} textAnchor="middle" className="fill-slate-500 text-[10px]">{formatDay(d.date)}</text>
            )}
          </g>
        );
      })}
    </svg>
  );
}

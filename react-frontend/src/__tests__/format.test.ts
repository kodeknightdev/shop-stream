import { formatMoney } from '../lib/format';
import { toQuery } from '../api/client';

describe('formatMoney', () => {
  it('formats USD', () => {
    expect(formatMoney(1234.5)).toBe('$1,234.50');
  });
});

describe('toQuery', () => {
  it('drops empty values', () => {
    expect(toQuery({ search: 'lamp', page: 2, category: '', minPrice: undefined })).toBe('?search=lamp&page=2');
    expect(toQuery({})).toBe('');
  });
});

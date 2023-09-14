import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ProductCard } from '../components/ProductCard';

it('shows name, category, price and links to the detail page', () => {
  render(
    <MemoryRouter>
      <ProductCard product={{ id: 'p1', sku: 'ELE-1001', name: 'Aurora Wireless Headphones', slug: 'aurora', price: 129.99, currency: 'USD',
        imageUrl: 'https://img/1', categoryId: 'c1', categoryName: 'Electronics', isActive: true }} />
    </MemoryRouter>,
  );
  expect(screen.getByText('Aurora Wireless Headphones')).toBeInTheDocument();
  expect(screen.getByText('Electronics')).toBeInTheDocument();
  expect(screen.getByText('$129.99')).toBeInTheDocument();
  expect(screen.getByRole('link')).toHaveAttribute('href', '/products/p1');
});

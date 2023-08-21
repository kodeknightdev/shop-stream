import { api, toQuery } from './client';
import type {
  AuthResponse, Cart, Category, OrderDetail, OrderDetailsView, OrderSummary, Paged, ProductDetail, ProductSummary, SalesSummary,
  ShippingAddress, StockItemAdmin, StockLevel, User,
} from './types';

export interface ProductFilters {
  search?: string;
  category?: string;
  minPrice?: number;
  maxPrice?: number;
  sort?: string;
  page?: number;
  pageSize?: number;
  includeInactive?: boolean;
}

export interface ProductInput {
  categoryId: string;
  sku: string;
  name: string;
  description: string;
  price: number;
  imageUrl: string;
  attributes: Record<string, string>;
  isActive: boolean;
}

export const authApi = {
  login: (email: string, password: string) => api<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, auth: false }),
  register: (email: string, password: string, fullName: string) =>
    api<AuthResponse>('/api/auth/register', { method: 'POST', body: { email, password, fullName }, auth: false }),
  logout: (refreshToken: string) => api<void>('/api/auth/logout', { method: 'POST', body: { refreshToken }, auth: false }),
  me: () => api<User>('/api/auth/me'),
};

export const catalogApi = {
  categories: () => api<Category[]>('/api/categories', { auth: false }),
  products: (f: ProductFilters) => api<Paged<ProductSummary>>(`/api/products${toQuery({ ...f })}`, { auth: !!f.includeInactive }),
  product: (id: string) => api<ProductDetail>(`/api/products/${id}`, { auth: false }),
  createProduct: (input: ProductInput) => api<ProductDetail>('/api/products', { method: 'POST', body: input }),
  updateProduct: (id: string, input: ProductInput) => api<ProductDetail>(`/api/products/${id}`, { method: 'PUT', body: input }),
  deactivateProduct: (id: string) => api<void>(`/api/products/${id}`, { method: 'DELETE' }),
};

export const inventoryApi = {
  stock: (productId: string) => api<StockLevel>(`/api/inventory/${productId}`, { auth: false }),
  availability: (productIds: string[]) => api<StockLevel[]>('/api/inventory/availability', { method: 'POST', body: { productIds }, auth: false }),
  list: (lowStock: boolean, page = 1) => api<Paged<StockItemAdmin>>(`/api/inventory${toQuery({ lowStock, page, pageSize: 100 })}`),
  adjust: (productId: string, delta: number, reason: string, sku?: string) =>
    api<StockItemAdmin>(`/api/inventory/${productId}/adjust`, { method: 'POST', body: { delta, reason, sku } }),
};

export const cartApi = {
  get: () => api<Cart>('/api/cart'),
  add: (productId: string, quantity = 1) => api<Cart>('/api/cart/items', { method: 'POST', body: { productId, quantity } }),
  update: (productId: string, quantity: number) => api<Cart>(`/api/cart/items/${productId}`, { method: 'PUT', body: { quantity } }),
  remove: (productId: string) => api<Cart>(`/api/cart/items/${productId}`, { method: 'DELETE' }),
  validate: () => api<{ cart: Cart; changes: unknown[]; removed: unknown[] }>('/api/cart/validate', { method: 'POST' }),
};

export const ordersApi = {
  place: (shippingAddress: ShippingAddress, paymentMethod: string, idempotencyKey: string) =>
    api<OrderDetail>('/api/orders', { method: 'POST', body: { shippingAddress, paymentMethod }, headers: { 'Idempotency-Key': idempotencyKey } }),
  mine: (page = 1) => api<Paged<OrderSummary>>(`/api/orders${toQuery({ page })}`),
  get: (id: string) => api<OrderDetail>(`/api/orders/${id}`),
  details: (id: string) => api<OrderDetailsView>(`/api/orders/${id}/details`),
  cancel: (id: string) => api<OrderDetail>(`/api/orders/${id}/cancel`, { method: 'POST' }),
};

export const adminApi = {
  orders: (f: { status?: string; email?: string; from?: string; to?: string; page?: number }) =>
    api<Paged<OrderSummary>>(`/api/admin/orders${toQuery(f)}`),
  salesSummary: (days: number) => api<SalesSummary>(`/api/admin/sales-summary${toQuery({ days })}`),
};

export const paymentsApi = {
  provider: () => api<{ provider: string }>('/api/payments/provider', { auth: false }),
};

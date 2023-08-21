export type Role = 'Customer' | 'Admin';

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: Role;
  createdAt: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: User;
}

export interface Category {
  id: string;
  name: string;
  slug: string;
  description?: string;
  productCount: number;
}

export interface ProductSummary {
  id: string;
  sku: string;
  name: string;
  slug: string;
  price: number;
  currency: string;
  imageUrl: string;
  categoryId: string;
  categoryName: string;
  isActive: boolean;
}

export interface ProductDetail extends ProductSummary {
  description: string;
  categorySlug: string;
  attributes: Record<string, string>;
  createdAt: string;
  updatedAt: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages?: number;
}

export interface StockLevel {
  productId: string;
  sku: string;
  available: number;
  inStock: boolean;
  lowStock: boolean;
}

export interface StockItemAdmin extends StockLevel {
  onHand: number;
  reserved: number;
  lowStockThreshold: number;
  updatedAt: string;
}

export interface CartItem {
  productId: string;
  sku: string;
  name: string;
  imageUrl: string;
  unitPrice: number;
  quantity: number;
  priceStale: boolean;
  latestPrice?: number;
}

export interface Cart {
  userId: string;
  items: CartItem[];
  itemCount: number;
  subtotal: number;
  hasStalePrices: boolean;
  updatedAt: string | null;
}

export type OrderStatus = 'Pending' | 'InventoryReserved' | 'Paid' | 'Confirmed' | 'Cancelled';

export interface ShippingAddress {
  fullName: string;
  line1: string;
  line2?: string;
  city: string;
  postalCode: string;
  country: string;
}

export interface OrderSummary {
  id: string;
  number: string;
  status: OrderStatus;
  total: number;
  currency: string;
  itemCount: number;
  createdAt: string;
  email: string;
}

export interface OrderItem {
  productId: string;
  sku: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  imageUrl?: string;
}

export interface StatusChange {
  from: OrderStatus | null;
  to: OrderStatus;
  note: string;
  occurredAt: string;
}

export interface PriceAdjustment {
  productId: string;
  name: string;
  cartPrice: number;
  catalogPrice: number;
}

export interface OrderDetail {
  id: string;
  number: string;
  status: OrderStatus;
  total: number;
  currency: string;
  email: string;
  paymentMethod: string;
  shippingAddress: ShippingAddress;
  items: OrderItem[];
  timeline: StatusChange[];
  cancellationReason?: string | null;
  canCancel: boolean;
  createdAt: string;
  updatedAt: string;
  priceAdjustments?: PriceAdjustment[] | null;
}

export interface SalesDay {
  date: string;
  orders: number;
  confirmedOrders: number;
  cancelledOrders: number;
  revenue: number;
}

export interface SalesSummary {
  days: number;
  totalOrders: number;
  confirmedOrders: number;
  revenue: number;
  averageOrderValue: number;
  daily: SalesDay[];
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]> | string[];
  correlationId?: string;
}

export interface PaymentInfo {
  id: string;
  orderId: string;
  amount: number;
  currency: string;
  provider: string;
  status: 'Succeeded' | 'Failed';
  failureReason?: string | null;
  providerReference?: string | null;
  createdAt: string;
}

export interface ProductSnapshot {
  productId: string;
  orderedName: string;
  orderedUnitPrice: number;
  quantity: number;
  currentName?: string | null;
  currentPrice?: number | null;
  imageUrl?: string | null;
  stillAvailable?: boolean | null;
}

// GET /api/orders/{id}/details
export interface OrderDetailsView {
  order: OrderDetail;
  payment: PaymentInfo | null;
  products: ProductSnapshot[];
  warnings: string[];
}

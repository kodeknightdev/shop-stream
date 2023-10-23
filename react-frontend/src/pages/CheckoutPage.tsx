import { FormEvent, useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { cartApi, ordersApi, paymentsApi } from '../api/endpoints';
import type { ShippingAddress } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage, Spinner } from '../components/ErrorMessage';
import { clearCheckoutKey, getCheckoutKey } from '../lib/checkoutKey';
import { formatMoney } from '../lib/format';

// Stripe test cards (the mock provider knows the same ids)
export const TEST_CARDS = [
  { id: 'pm_card_visa', label: 'Visa 4242 4242 4242 4242 - succeeds' },
  { id: 'pm_card_mastercard', label: 'Mastercard 5555 5555 5555 4444 - succeeds' },
  { id: 'pm_card_chargeDeclined', label: 'Visa 4000 0000 0000 0002 - declined' },
  { id: 'pm_card_visa_chargeDeclinedInsufficientFunds', label: 'Visa 4000 0000 0000 9995 - insufficient funds' },
];

export function CheckoutPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const cart = useQuery({ queryKey: ['cart'], queryFn: cartApi.get });
  const provider = useQuery({ queryKey: ['payment-provider'], queryFn: paymentsApi.provider, retry: false });
  const [address, setAddress] = useState<ShippingAddress>({
    fullName: user?.fullName ?? '', line1: '', line2: '', city: '', postalCode: '', country: 'US',
  });
  const [paymentMethod, setPaymentMethod] = useState(TEST_CARDS[0].id);

  const place = useMutation({
    mutationFn: () => ordersApi.place({ ...address, line2: address.line2 || undefined }, paymentMethod,
      getCheckoutKey(user!.id, cart.data?.items ?? [])),
    onSuccess: (order) => {
      clearCheckoutKey();
      queryClient.invalidateQueries({ queryKey: ['cart'] });
      queryClient.invalidateQueries({ queryKey: ['orders'] });
      navigate(`/orders/${order.id}`, { state: { justPlaced: true, adjustments: order.priceAdjustments } });
    },
  });

  if (cart.isLoading) return <Spinner />;
  if (cart.data && cart.data.items.length === 0 && !place.isSuccess) return <Navigate to="/cart" replace />;

  const field = (key: keyof ShippingAddress, label: string, props: Record<string, unknown> = {}) => (
    <div>
      <label className="label" htmlFor={key}>{label}</label>
      <input id={key} className="input" value={address[key] ?? ''} onChange={(e) => setAddress({ ...address, [key]: e.target.value })} {...props} />
    </div>
  );

  const submit = (e: FormEvent) => {
    e.preventDefault();
    place.mutate();
  };

  return (
    <form onSubmit={submit} className="grid gap-8 lg:grid-cols-[1fr_320px]">
      <section className="space-y-6">
        <h1 className="text-2xl font-bold">Checkout</h1>
        <fieldset className="card grid gap-4 p-5 sm:grid-cols-2">
          <legend className="px-1 text-sm font-semibold">Shipping address</legend>
          <div className="sm:col-span-2">{field('fullName', 'Full name', { required: true })}</div>
          <div className="sm:col-span-2">{field('line1', 'Address', { required: true })}</div>
          <div className="sm:col-span-2">{field('line2', 'Apartment, suite (optional)')}</div>
          {field('city', 'City', { required: true })}
          {field('postalCode', 'Postal code', { required: true })}
          {field('country', 'Country (2-letter code)', { required: true, maxLength: 2, minLength: 2 })}
        </fieldset>
        <fieldset className="card space-y-2 p-5">
          <legend className="px-1 text-sm font-semibold">Payment</legend>
          <p className="text-xs text-slate-500">
            {provider.data?.provider === 'stripe' ? 'Stripe test mode' : 'Offline mock payments'} - no real money is charged.
          </p>
          {TEST_CARDS.map((card) => (
            <label key={card.id} className="flex items-center gap-2 text-sm">
              <input type="radio" name="card" value={card.id} checked={paymentMethod === card.id} onChange={() => setPaymentMethod(card.id)} />
              {card.label}
            </label>
          ))}
        </fieldset>
        <ErrorMessage error={place.error} />
      </section>
      <aside className="card h-fit space-y-3 p-5">
        <h2 className="font-semibold">Order summary</h2>
        {cart.data?.items.map((i) => (
          <div key={i.productId} className="flex justify-between text-sm">
            <span>{i.quantity} x {i.name}</span>
            <span>{formatMoney(i.unitPrice * i.quantity)}</span>
          </div>
        ))}
        <div className="flex justify-between border-t pt-3 font-semibold"><span>Total</span><span>{formatMoney(cart.data?.subtotal ?? 0)}</span></div>
        <button type="submit" className="btn-primary w-full" disabled={place.isLoading}>{place.isLoading ? 'Placing order...' : 'Place order'}</button>
      </aside>
    </form>
  );
}

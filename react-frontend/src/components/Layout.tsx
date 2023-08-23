import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../auth/AuthContext';
import { cartApi } from '../api/endpoints';

const navClass = ({ isActive }: { isActive: boolean }) =>
  `text-sm ${isActive ? 'font-semibold text-brand-700' : 'text-slate-600 hover:text-slate-900'}`;

export function Layout() {
  const { user, isAdmin, logout } = useAuth();
  const navigate = useNavigate();
  const { data: cart } = useQuery({ queryKey: ['cart'], queryFn: cartApi.get, enabled: !!user });

  return (
    <div className="flex min-h-screen flex-col">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl items-center gap-6 px-4 py-3">
          <Link to="/" className="text-xl font-bold text-brand-700">shop-stream</Link>
          <nav className="flex flex-1 items-center gap-5">
            <NavLink to="/" end className={navClass}>Shop</NavLink>
            {user && <NavLink to="/orders" className={navClass}>Orders</NavLink>}
            {isAdmin && <NavLink to="/admin" className={navClass}>Admin</NavLink>}
          </nav>
          {user ? (
            <div className="flex items-center gap-4">
              <NavLink to="/cart" className={navClass}>
                Cart{cart && cart.itemCount > 0 && <span className="ml-1 rounded-full bg-brand-700 px-2 py-0.5 text-xs text-white">{cart.itemCount}</span>}
              </NavLink>
              <span className="hidden text-sm text-slate-500 sm:inline">{user.fullName}</span>
              <button className="btn-secondary" onClick={async () => { await logout(); navigate('/'); }}>Sign out</button>
            </div>
          ) : (
            <div className="flex items-center gap-3">
              <Link to="/login" className="btn-secondary">Sign in</Link>
              <Link to="/register" className="btn-primary">Create account</Link>
            </div>
          )}
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8">
        <Outlet />
      </main>
      <footer className="border-t border-slate-200 bg-white py-4 text-center text-xs text-slate-500">
        shop-stream demo store · payments run in Stripe test mode
      </footer>
    </div>
  );
}

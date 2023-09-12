import { NavLink, Outlet } from 'react-router-dom';

const tab = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-1.5 text-sm ${isActive ? 'bg-brand-700 text-white' : 'text-slate-600 hover:bg-slate-200'}`;

export function AdminLayout() {
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-2">
        <h1 className="mr-4 text-2xl font-bold">Admin</h1>
        <NavLink to="/admin" end className={tab}>Dashboard</NavLink>
        <NavLink to="/admin/orders" className={tab}>Orders</NavLink>
        <NavLink to="/admin/products" className={tab}>Products</NavLink>
        <NavLink to="/admin/inventory" className={tab}>Inventory</NavLink>
      </div>
      <Outlet />
    </div>
  );
}

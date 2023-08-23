import { Link } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <div className="py-16 text-center">
      <h1 className="mb-2 text-3xl font-bold">Page not found</h1>
      <Link to="/" className="text-brand-700 hover:underline">Back to the shop</Link>
    </div>
  );
}

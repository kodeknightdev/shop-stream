import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from '../components/ErrorMessage';

export function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [form, setForm] = useState({ fullName: '', email: '', password: '' });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await register(form.email, form.password, form.fullName);
      navigate('/', { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const input = (key: keyof typeof form, label: string, type = 'text') => (
    <div>
      <label className="label" htmlFor={key}>{label}</label>
      <input id={key} type={type} required className="input" value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
    </div>
  );

  return (
    <form onSubmit={submit} className="card mx-auto max-w-sm space-y-4 p-6">
      <h1 className="text-xl font-bold">Create an account</h1>
      {input('fullName', 'Full name')}
      {input('email', 'Email', 'email')}
      {input('password', 'Password', 'password')}
      <p className="text-xs text-slate-500">At least 8 characters with upper- and lower-case letters and a digit.</p>
      <ErrorMessage error={error} />
      <button type="submit" className="btn-primary w-full" disabled={busy}>{busy ? 'Creating...' : 'Create account'}</button>
      <p className="text-center text-sm text-slate-500">Have an account? <Link to="/login" className="text-brand-700 hover:underline">Sign in</Link></p>
    </form>
  );
}

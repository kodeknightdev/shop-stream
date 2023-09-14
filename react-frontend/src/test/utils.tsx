import { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { AuthProvider } from '../auth/AuthContext';

export function renderWithProviders(ui: ReactElement, { route = '/', path = '*' }: { route?: string; path?: string } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } }, logger: { log: () => {}, warn: () => {}, error: () => {} } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>
          <Routes>
            <Route path={path} element={ui} />
            <Route path="/login" element={<div>login page</div>} />
            <Route path="/admin" element={<div>admin home</div>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

type Handler = (url: string, init?: RequestInit) => { status?: number; body?: unknown } | undefined;

// unmatched requests get a 404
export function mockFetch(handler: Handler) {
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();
    const result = handler(url, init) ?? { status: 404, body: { title: 'Not found', status: 404 } };
    const status = result.status ?? 200;
    return new Response(status === 204 ? null : JSON.stringify(result.body ?? {}), {
      status,
      headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
    });
  });
  vi.stubGlobal('fetch', fn);
  return fn;
}

export function signIn(role: 'Customer' | 'Admin' = 'Customer') {
  localStorage.setItem('shop-stream.auth', JSON.stringify({
    accessToken: 'access', refreshToken: 'refresh', expiresAt: '', refreshTokenExpiresAt: '',
    user: { id: 'u1', email: 'casey@example.com', fullName: 'Casey Customer', role, createdAt: '' },
  }));
}

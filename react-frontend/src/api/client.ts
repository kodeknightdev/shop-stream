import type { AuthResponse, ProblemDetails } from './types';

const BASE = import.meta.env.VITE_API_BASE ?? '';
const STORAGE_KEY = 'shop-stream.auth';

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly problem: ProblemDetails) {
    super(problem.detail || problem.title || `Request failed with ${status}`);
  }

  get messages(): string[] {
    const errors = this.problem.errors;
    if (!errors) return [this.message];
    if (Array.isArray(errors)) return errors;
    return Object.entries(errors).flatMap(([, list]) => list);
  }
}

export const tokenStore = {
  get(): AuthResponse | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as AuthResponse) : null;
    } catch {
      return null;
    }
  },
  set(auth: AuthResponse | null) {
    if (auth) localStorage.setItem(STORAGE_KEY, JSON.stringify(auth));
    else localStorage.removeItem(STORAGE_KEY);
    window.dispatchEvent(new Event('shop-stream:auth'));
  },
};

let refreshing: Promise<AuthResponse | null> | null = null;

async function refreshTokens(): Promise<AuthResponse | null> {
  const current = tokenStore.get();
  if (!current) return null;
  refreshing ??= fetch(`${BASE}/api/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: current.refreshToken }),
  })
    .then(async (res) => {
      const next = res.ok ? ((await res.json()) as AuthResponse) : null;
      tokenStore.set(next);
      return next;
    })
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown;
  auth?: boolean;
}

// json in/out, bearer token, one refresh + retry on 401
export async function api<T>(path: string, { body, auth = true, headers, ...init }: RequestOptions = {}): Promise<T> {
  const send = (token?: string) =>
    fetch(`${BASE}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });

  let res = await send(auth ? tokenStore.get()?.accessToken : undefined);
  if (res.status === 401 && auth && tokenStore.get()) {
    const next = await refreshTokens();
    if (next) res = await send(next.accessToken);
  }

  if (res.status === 204) return undefined as T;
  const contentType = res.headers.get('content-type') ?? '';
  const payload = contentType.includes('json') ? await res.json() : await res.text();

  if (!res.ok) {
    const problem: ProblemDetails = typeof payload === 'object' ? payload : { title: res.statusText, status: res.status };
    throw new ApiError(res.status, problem);
  }
  return payload as T;
}

export function toQuery(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
}

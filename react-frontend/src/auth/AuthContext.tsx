import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { tokenStore } from '../api/client';
import { authApi } from '../api/endpoints';
import type { AuthResponse, User } from '../api/types';

interface AuthState {
  user: User | null;
  isAdmin: boolean;
  login: (email: string, password: string) => Promise<User>;
  register: (email: string, password: string, fullName: string) => Promise<User>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthResponse | null>(() => tokenStore.get());
  const queryClient = useQueryClient();

  useEffect(() => {
    // keep other tabs / the api client in sync
    const sync = () => setAuth(tokenStore.get());
    window.addEventListener('shop-stream:auth', sync);
    window.addEventListener('storage', sync);
    return () => {
      window.removeEventListener('shop-stream:auth', sync);
      window.removeEventListener('storage', sync);
    };
  }, []);

  const accept = useCallback((next: AuthResponse) => {
    tokenStore.set(next);
    setAuth(next);
    queryClient.invalidateQueries();
    return next.user;
  }, [queryClient]);

  const value = useMemo<AuthState>(() => ({
    user: auth?.user ?? null,
    isAdmin: auth?.user.role === 'Admin',
    login: async (email, password) => accept(await authApi.login(email, password)),
    register: async (email, password, fullName) => accept(await authApi.register(email, password, fullName)),
    logout: async () => {
      const current = tokenStore.get();
      tokenStore.set(null);
      setAuth(null);
      queryClient.clear();
      if (current) await authApi.logout(current.refreshToken).catch(() => undefined);
    },
  }), [auth, accept, queryClient]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}

import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import { authApi } from '../api/auth';
import type { LoginRequest, UserInfo } from '../types';

interface AuthContextType {
  user: UserInfo | null;
  token: string | null;
  permissions: string[];
  loading: boolean;
  login: (data: LoginRequest) => Promise<void>;
  logout: () => void;
  hasPermission: (perm: string) => boolean;
  hasRole: (role: string) => boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [token, setToken] = useState<string | null>(localStorage.getItem('token'));
  const [permissions, setPermissions] = useState<string[]>(() => {
    try { return JSON.parse(localStorage.getItem('permissions') || '[]'); }
    catch { return []; }
  });
  const [loading, setLoading] = useState(true);

  const fetchUser = useCallback(async () => {
    if (!token) {
      setLoading(false);
      return;
    }
    try {
      const res = await authApi.getCurrentUser();
      setUser(res.data.data ?? null);
    } catch {
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      localStorage.removeItem('permissions');
      setToken(null);
      setUser(null);
      setPermissions([]);
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    fetchUser();
  }, [fetchUser]);

  const login = async (data: LoginRequest) => {
    const res = await authApi.login(data);
    const result = res.data.data!;
    localStorage.setItem('token', result.token);
    localStorage.setItem('permissions', JSON.stringify(result.permissions));
    setToken(result.token);
    setPermissions(result.permissions);
    try {
      const userRes = await authApi.getCurrentUser();
      setUser(userRes.data.data ?? null);
    } catch {
      // If getting user info fails, still set basic info from login response
      setUser({
        id: 0,
        username: data.username,
        displayName: result.displayName,
        email: '',
        isActive: true,
        roles: result.roles,
        authType: 'Local',
      });
    }
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('permissions');
    localStorage.removeItem('user');
    setToken(null);
    setUser(null);
    setPermissions([]);
  };

  const hasPermission = (perm: string) => permissions.includes(perm);
  const hasRole = (role: string) => user?.roles.includes(role) ?? false;

  return (
    <AuthContext.Provider value={{ user, token, permissions, loading, login, logout, hasPermission, hasRole }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

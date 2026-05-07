import { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ConfigProvider, Spin } from 'antd';
import enUS from 'antd/es/locale/en_US';
import zhCN from 'antd/es/locale/zh_CN';
import dayjs from 'dayjs';
import 'dayjs/locale/zh-cn';
import { AuthProvider, useAuth } from './context/AuthContext';
import { LocaleProvider, useLocale } from './context/LocaleContext';
import AppLayout from './components/AppLayout';
import LoginPage from './pages/Login/LoginPage';
import DashboardPage from './pages/Dashboard/DashboardPage';
import ImportPage from './pages/Import/ImportPage';
import ImportLogsPage from './pages/ImportLogs/ImportLogsPage';
import UsersPage from './pages/Users/UsersPage';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { token, loading } = useAuth();

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <Spin size="large" />
      </div>
    );
  }

  if (!token) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

function AppRoutes() {
  const { token } = useAuth();
  const { lang } = useLocale();

  useEffect(() => {
    dayjs.locale(lang === 'zh' ? 'zh-cn' : 'en');
  }, [lang]);

  const antdLocale = lang === 'zh' ? zhCN : enUS;

  return (
    <ConfigProvider locale={antdLocale}
      theme={{
        token: {
          colorPrimary: '#1890ff',
          borderRadius: 6,
        },
      }}
    >
      <Routes>
      <Route
        path="/login"
        element={token ? <Navigate to="/" replace /> : <LoginPage />}
      />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <AppLayout />
          </ProtectedRoute>
        }
      >
        <Route index element={<DashboardPage />} />
        <Route path="import" element={<ImportPage />} />
        <Route path="import-logs" element={<ImportLogsPage />} />
        <Route path="users" element={<UsersPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
    </ConfigProvider>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <LocaleProvider>
          <AppRoutes />
        </LocaleProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

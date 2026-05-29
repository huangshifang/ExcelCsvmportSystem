import { useTranslation } from 'react-i18next';
import { useLocale } from '../context/LocaleContext';
import { useState } from 'react';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { Layout, Menu, Button, Dropdown, theme, Avatar } from 'antd';
import {
  UploadOutlined,
  FileTextOutlined,
  UserOutlined,
  DashboardOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  GlobalOutlined,
  CheckCircleOutlined,
  SettingOutlined,
  KeyOutlined,
  AuditOutlined,
  CloudServerOutlined,
} from '@ant-design/icons';
import { useAuth } from '../context/AuthContext';
import ChangePasswordModal from './ChangePasswordModal';

const { Header, Sider, Content } = Layout;

export default function AppLayout() {
  const [collapsed, setCollapsed] = useState(false);
  const [changePasswordOpen, setChangePasswordOpen] = useState(false);
  const { user, logout, hasPermission } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const { token: themeToken } = theme.useToken();
  const { t } = useTranslation();
  const { lang, setLang } = useLocale();

  const menuItems = [
    {
      key: '/',
      icon: <DashboardOutlined />,
      label: t('nav.dashboard'),
    },
    ...(hasPermission('Import.Execute')
      ? [
          {
            key: '/import',
            icon: <UploadOutlined />,
            label: t('nav.import'),
          },
        ]
      : []),
    ...(hasPermission('Import.View')
      ? [
          {
            key: '/import-logs',
            icon: <FileTextOutlined />,
            label: t('nav.importLogs'),
          },
        ]
      : []),
    ...(hasPermission('Audit.View')
      ? [
          {
            key: '/login-logs',
            icon: <AuditOutlined />,
            label: t('nav.loginAudit'),
          },
        ]
      : []),
    ...(hasPermission('Server.View')
      ? [{
            key: '/servers',
            icon: <CloudServerOutlined />,
            label: t('nav.servers'),
          }]
      : []),
    ...(hasPermission('User.Manage')
      ? [
          {
            key: '/users',
            icon: <UserOutlined />,
            label: t('nav.userManagement'),
          },
        ]
      : []),
    ...(hasPermission('System.Manage')
      ? [
          {
            key: '/system',
            icon: <SettingOutlined />,
            label: t('nav.systemSettings'),
          },
        ]
      : []),
  ];

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  const userMenu = {
    items: [
      { key: 'profile', label: `${user?.displayName} (${user?.username})`, disabled: true },
      { type: 'divider' as const },
      ...(user?.authType !== 'LDAP'
        ? [{ key: 'changePassword', label: t('changePassword.title'), icon: <KeyOutlined /> }]
        : []),
      { key: 'logout', label: t('header.logout'), icon: <LogoutOutlined />, danger: true },
    ],
    onClick: ({ key }: { key: string }) => {
      if (key === 'logout') handleLogout();
      if (key === 'changePassword') setChangePasswordOpen(true);
    },
  };

  const langMenu = {
    items: [
      { key: 'en', label: 'English', icon: lang === 'en' ? <CheckCircleOutlined /> : null as any },
      { key: 'zh', label: '中文', icon: lang === 'zh' ? <CheckCircleOutlined /> : null as any },
    ],
    onClick: ({ key }: { key: string }) => setLang(key as 'en' | 'zh'),
  };

  if (!user) return null;

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        trigger={null}
        collapsible
        collapsed={collapsed}
        theme="light"
        style={{
          borderRight: `1px solid ${themeToken.colorBorderSecondary}`,
        }}
      >
        <div
          style={{
            height: 64,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            borderBottom: `1px solid ${themeToken.colorBorderSecondary}`,
            fontWeight: 600,
            fontSize: collapsed ? 14 : 18,
          }}
        >
          {collapsed ? t('app.titleShort') : t('app.title')}
        </div>
        <Menu
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
        <div
          style={{
            position: 'absolute',
            bottom: 24,
            left: 0,
            right: 0,
            textAlign: 'center',
            fontSize: 12,
            color: themeToken.colorTextTertiary,
            padding: '0 16px',
          }}
        >
          {t('app.copyright')}
        </div>
      </Sider>
      <Layout>
        <Header
          style={{
            padding: '0 24px',
            background: themeToken.colorBgContainer,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: `1px solid ${themeToken.colorBorderSecondary}`,
          }}
        >
          <Button
            type="text"
            icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setCollapsed(!collapsed)}
          />
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Dropdown menu={langMenu} placement="bottomRight">
              <Button type="text" icon={<GlobalOutlined />}>
                {lang === 'zh' ? '中文' : 'EN'}
              </Button>
            </Dropdown>
            <Dropdown menu={userMenu} placement="bottomRight">
              <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8 }}>
                <Avatar icon={<UserOutlined />} />
                <span>{user.displayName}</span>
              </div>
            </Dropdown>
          </div>
        </Header>
        <Content style={{ margin: 24 }}>
          <Outlet />
        </Content>
      </Layout>
      <ChangePasswordModal
        open={changePasswordOpen}
        onClose={() => setChangePasswordOpen(false)}
      />
    </Layout>
  );
}

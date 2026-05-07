import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Form, Input, Button, Card, Typography, message, Space } from 'antd';
import { UserOutlined, LockOutlined, FileExcelOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';

const { Title, Text } = Typography;

export default function LoginPage() {
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();
  const { t } = useTranslation();

  const onFinish = async (values: { username: string; password: string }) => {
    setLoading(true);
    try {
      await login(values);
      message.success(t('login.title') + ' ' + t('common.success'));
      navigate('/');
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        t('login.error');
      message.error(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        background: '#f0f2f5',
      }}
    >
      <Card style={{ width: 400, boxShadow: '0 2px 8px rgba(0,0,0,0.1)' }}>
        <Space direction="vertical" style={{ width: '100%', textAlign: 'center', marginBottom: 24 }}>
          <FileExcelOutlined style={{ fontSize: 48, color: '#52c41a' }} />
          <Title level={3} style={{ margin: 0 }}>
            {t('app.title')}
          </Title>
          <Text type="secondary">{t('login.title')}</Text>
        </Space>
        <Form name="login" onFinish={onFinish} layout="vertical" size="large">
          <Form.Item name="username" rules={[{ required: true, message: t('login.title') }]}>
            <Input prefix={<UserOutlined />} placeholder={t('login.username')} />
          </Form.Item>
          <Form.Item name="password" rules={[{ required: true, message: t('login.title') }]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('login.password')} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              {t('login.submit')}
            </Button>
          </Form.Item>
          <Text type="secondary" style={{ display: 'block', textAlign: 'center', fontSize: 12 }}>
            {t('login.hint')}
          </Text>
        </Form>
      </Card>
    </div>
  );
}

import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Form, Input, Button, Card, Typography, message, Space } from 'antd';
import { UserOutlined, LockOutlined, ReloadOutlined, SafetyOutlined, FileExcelOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { authApi } from '../../api/auth';

const { Title } = Typography;

export default function LoginPage() {
  const [loading, setLoading] = useState(false);
  const [captchaImage, setCaptchaImage] = useState('');
  const [captchaToken, setCaptchaToken] = useState('');
  const [captchaLoading, setCaptchaLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();
  const { t } = useTranslation();

  const fetchCaptcha = useCallback(async () => {
    setCaptchaLoading(true);
    try {
      const res = await authApi.getCaptcha();
      const data = res.data.data;
      if (data) {
        setCaptchaImage(data.imageBase64);
        setCaptchaToken(data.token);
      }
    } catch {
      // captcha fetch failed silently
    } finally {
      setCaptchaLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchCaptcha();
  }, [fetchCaptcha]);

  const onFinish = async (values: { username: string; password: string; captchaCode: string }) => {
    setLoading(true);
    try {
      await login({
        username: values.username,
        password: values.password,
        captchaCode: values.captchaCode,
        captchaToken,
      });
      message.success(t('login.title') + ' ' + t('common.success'));
      navigate('/');
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        t('login.error');
      message.error(msg);
      fetchCaptcha();
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
      <Card style={{ width: 420, boxShadow: '0 2px 8px rgba(0,0,0,0.1)' }}>
        <Space direction="vertical" style={{ width: '100%', textAlign: 'center', marginBottom: 24 }}>
          <FileExcelOutlined style={{ fontSize: 48, color: '#52c41a' }} />
          <Title level={3} style={{ margin: 0 }}>
            {t('app.title')}
          </Title>
        </Space>
        <Form name="login" onFinish={onFinish} layout="vertical" size="large">
          <Form.Item name="username" rules={[{ required: true, message: t('login.username') }]}>
            <Input prefix={<UserOutlined />} placeholder={t('login.username')} autoComplete="off" />
          </Form.Item>
          <Form.Item name="password" rules={[{ required: true, message: t('login.password') }]}>
            <Input.Password prefix={<LockOutlined />} placeholder={t('login.password')} autoComplete="new-password" />
          </Form.Item>
          <Form.Item name="captchaCode" rules={[{ required: true, message: t('login.captchaPlaceholder') }]}>
            <div style={{ display: 'flex', gap: 8 }}>
              <Input
                prefix={<SafetyOutlined />}
                placeholder={t('login.captchaPlaceholder')}
                style={{ flex: 1 }}
                autoComplete="off"
              />
              <div
                style={{
                  width: 130,
                  height: 40,
                  cursor: 'pointer',
                  borderRadius: 6,
                  overflow: 'hidden',
                  border: '1px solid #d9d9d9',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  background: '#fafafa',
                  flexShrink: 0,
                }}
                onClick={() => fetchCaptcha()}
              >
                {captchaLoading ? (
                  <ReloadOutlined spin style={{ fontSize: 18, color: '#999' }} />
                ) : captchaImage ? (
                  <img
                    src={`data:image/svg+xml;base64,${captchaImage}`}
                    alt="captcha"
                    style={{ width: 128, height: 38, display: 'block' }}
                  />
                ) : (
                  <ReloadOutlined style={{ fontSize: 18, color: '#999' }} />
                )}
              </div>
            </div>
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              {t('login.submit')}
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
}

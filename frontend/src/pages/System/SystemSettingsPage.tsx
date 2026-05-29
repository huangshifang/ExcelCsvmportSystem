import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Form, Input, InputNumber, Switch, Button, Typography,
  message, Spin, Space, Divider, Alert, Tag
} from 'antd';
import {
  SettingOutlined, ExperimentOutlined, SaveOutlined,
  CheckCircleOutlined, CloseCircleOutlined
} from '@ant-design/icons';
import { systemSettingsApi } from '../../api/systemSettings';
import type { LdapSettings } from '../../types';

const { Title, Text } = Typography;

export default function SystemSettingsPage() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{
    success: boolean;
    dn?: string;
    displayName?: string;
    email?: string;
  } | null>(null);
  const [settings, setSettings] = useState<LdapSettings>({
    enabled: false,
    server: '',
    port: 389,
    useSsl: false,
    domain: '',
    baseDn: '',
    userFilterTemplate: '(sAMAccountName={0})',
    bindUserDn: '',
    bindPassword: '',
    bindPasswordSet: false,
    testUsername: '',
    testPassword: '',
  });

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    setLoading(true);
    try {
      const res = await systemSettingsApi.getLdapSettings();
      if (res.data.data) {
        setSettings((prev) => ({
          ...prev,
          ...res.data.data,
          bindPassword: '',
          testUsername: '',
          testPassword: '',
        }));
      }
    } catch {
      message.error(t('system.loadFailed'));
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await systemSettingsApi.updateLdapSettings(settings);
      message.success(t('system.saveSuccess'));
    } catch {
      message.error(t('system.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    if (!settings.testUsername || !settings.testPassword) {
      message.warning(t('system.testCredentialsRequired'));
      return;
    }
    setTesting(true);
    setTestResult(null);
    try {
      const res = await systemSettingsApi.testLdapConnection(settings);
      if (res.data.data) {
        setTestResult(res.data.data);
        if (res.data.data.success) {
          message.success(t('system.testSuccess'));
        } else {
          message.warning(t('system.testFailed'));
        }
      }
    } catch {
      message.error(t('system.testError'));
    } finally {
      setTesting(false);
    }
  };

  const update = (field: keyof LdapSettings, value: unknown) => {
    setSettings((prev) => ({ ...prev, [field]: value }));
  };

  return (
    <div>
      <Title level={4}>
        <SettingOutlined style={{ marginRight: 8 }} />
        {t('system.pageTitle')}
      </Title>

      <Spin spinning={loading}>
        <Card title={t('system.ldapTitle')} style={{ maxWidth: 720 }}>
          <Alert
            message={t('system.ldapHint')}
            type="info"
            showIcon
            style={{ marginBottom: 24 }}
          />

          <Form layout="vertical">
            <Form.Item label={t('system.ldapEnabled')}>
              <Switch
                checked={settings.enabled}
                onChange={(v) => update('enabled', v)}
              />
              <Text type="secondary" style={{ marginLeft: 8 }}>
                {settings.enabled ? t('system.enabled') : t('system.disabled')}
              </Text>
            </Form.Item>

            <Space size="large">
              <Form.Item label={t('system.ldapServer')}>
                <Input
                  value={settings.server}
                  onChange={(e) => update('server', e.target.value)}
                  placeholder="ldap.example.com"
                  style={{ width: 250 }}
                />
              </Form.Item>
              <Form.Item label={t('system.ldapPort')}>
                <InputNumber
                  value={settings.port}
                  onChange={(v) => update('port', v ?? 389)}
                  min={1}
                  max={65535}
                />
              </Form.Item>
              <Form.Item label="SSL">
                <Switch
                  checked={settings.useSsl}
                  onChange={(v) => update('useSsl', v)}
                />
              </Form.Item>
            </Space>

            <Form.Item label={t('system.ldapDomain')}>
              <Input
                value={settings.domain}
                onChange={(e) => update('domain', e.target.value)}
                placeholder="EXAMPLE"
                style={{ width: 300 }}
              />
            </Form.Item>

            <Form.Item label={t('system.ldapBaseDn')}>
              <Input
                value={settings.baseDn}
                onChange={(e) => update('baseDn', e.target.value)}
                placeholder="DC=example,DC=com"
                style={{ width: 400 }}
              />
            </Form.Item>

            <Form.Item label={t('system.ldapFilter')}>
              <Input
                value={settings.userFilterTemplate}
                onChange={(e) => update('userFilterTemplate', e.target.value)}
                style={{ width: 400 }}
              />
            </Form.Item>

            <Form.Item label={t('system.ldapBindUser')}>
              <Input
                value={settings.bindUserDn}
                onChange={(e) => update('bindUserDn', e.target.value)}
                placeholder="CN=svc,OU=Users,DC=example,DC=com"
                style={{ width: 400 }}
              />
            </Form.Item>

            <Form.Item label={t('system.ldapBindPassword')}>
              <Input.Password
                value={settings.bindPassword}
                onChange={(e) => update('bindPassword', e.target.value)}
                placeholder={settings.bindPasswordSet ? '(unchanged)' : ''}
                style={{ width: 300 }}
              />
            </Form.Item>

            <Divider />

            <div style={{ display: 'flex', gap: 12 }}>
              <Button
                type="primary"
                icon={<SaveOutlined />}
                loading={saving}
                onClick={handleSave}
              >
                {t('system.save')}
              </Button>
            </div>
          </Form>
        </Card>

        <Card title={t('system.testTitle')} style={{ maxWidth: 720, marginTop: 24 }}>
          <Form layout="vertical">
            <Space size="large">
              <Form.Item label={t('system.testUsername')} required>
                <Input
                  value={settings.testUsername}
                  onChange={(e) => update('testUsername', e.target.value)}
                  placeholder="username"
                  style={{ width: 200 }}
                />
              </Form.Item>
              <Form.Item label={t('system.testPassword')} required>
                <Input.Password
                  value={settings.testPassword}
                  onChange={(e) => update('testPassword', e.target.value)}
                  placeholder="password"
                  style={{ width: 200 }}
                />
              </Form.Item>
            </Space>
            <Button
              icon={<ExperimentOutlined />}
              loading={testing}
              onClick={handleTest}
            >
              {t('system.testButton')}
            </Button>

            {testResult && (
              <div style={{ marginTop: 16 }}>
                <Divider />
                <Space>
                  {testResult.success ? (
                    <Tag icon={<CheckCircleOutlined />} color="success">
                      {t('system.testSuccess')}
                    </Tag>
                  ) : (
                    <Tag icon={<CloseCircleOutlined />} color="error">
                      {t('system.testFailed')}
                    </Tag>
                  )}
                  {testResult.dn && <Text>DN: {testResult.dn}</Text>}
                  {testResult.displayName && <Text>Name: {testResult.displayName}</Text>}
                  {testResult.email && <Text>Email: {testResult.email}</Text>}
                </Space>
              </div>
            )}
          </Form>
        </Card>
      </Spin>
    </div>
  );
}

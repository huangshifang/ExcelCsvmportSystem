import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Table, Typography, Space, Tag, DatePicker,
  Input, Button, Select
} from 'antd';
import {
  AuditOutlined, SearchOutlined, ReloadOutlined,
  CheckCircleOutlined, CloseCircleOutlined
} from '@ant-design/icons';
import { authApi } from '../../api/auth';
import type { LoginAuditLog, PagedResult } from '../../types';
import dayjs from 'dayjs';

const { Title, Text } = Typography;
const { RangePicker } = DatePicker;

export default function LoginAuditPage() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<PagedResult<LoginAuditLog> | null>(null);
  const [page, setPage] = useState(1);
  const [username, setUsername] = useState('');
  const [status, setStatus] = useState<boolean | undefined>(undefined);
  const [dateRange, setDateRange] = useState<[string, string] | null>(null);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const res = await authApi.getLoginLogs({
        page,
        pageSize: 20,
        username: username || undefined,
        success: status,
        from: dateRange?.[0],
        to: dateRange?.[1],
      });
      setData(res.data.data ?? null);
    } catch {
      // handled by interceptor
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLogs();
  }, [page]);

  const handleSearch = () => {
    setPage(1);
    fetchLogs();
  };

  const handleReset = () => {
    setUsername('');
    setStatus(undefined);
    setDateRange(null);
    setPage(1);
  };

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 60 },
    { title: t('login.username'), dataIndex: 'username', key: 'username' },
    {
      title: t('logs.status'),
      dataIndex: 'success',
      key: 'success',
      width: 90,
      render: (v: boolean) =>
        v ? (
          <Tag icon={<CheckCircleOutlined />} color="success">{t('logs.statusSuccess')}</Tag>
        ) : (
          <Tag icon={<CloseCircleOutlined />} color="error">{t('logs.statusFailed')}</Tag>
        ),
    },
    {
      title: t('loginAudit.failureReason'),
      dataIndex: 'failureReason',
      key: 'failureReason',
      render: (v: string | null) => v ? <Text type="danger">{v}</Text> : <Text type="secondary">--</Text>,
    },
    { title: t('loginAudit.ipAddress'), dataIndex: 'ipAddress', key: 'ipAddress', render: (v: string | null) => v ?? '--' },
    {
      title: t('loginAudit.userAgent'),
      dataIndex: 'userAgent',
      key: 'userAgent',
      ellipsis: true,
      render: (v: string | null) => v ?? '--',
    },
    {
      title: t('logs.date'),
      dataIndex: 'timestamp',
      key: 'timestamp',
      width: 170,
      render: (v: string) => dayjs(v + 'Z').format('YYYY-MM-DD HH:mm:ss'),
    },
  ];

  return (
    <div>
      <Title level={4}>
        <AuditOutlined style={{ marginRight: 8 }} />
        {t('loginAudit.pageTitle')}
      </Title>

      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('loginAudit.searchUsername')}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            style={{ width: 200 }}
            onPressEnter={handleSearch}
          />
          <Select
            placeholder={t('logs.status')}
            value={status}
            onChange={(v) => setStatus(v)}
            allowClear
            style={{ width: 120 }}
            options={[
              { label: t('logs.statusSuccess'), value: true },
              { label: t('logs.statusFailed'), value: false },
            ]}
          />
          <RangePicker
            showTime
            onChange={(_, dateStrings) => setDateRange(dateStrings[0] && dateStrings[1] ? [dateStrings[0], dateStrings[1]] : null)}
          />
          <Button type="primary" icon={<SearchOutlined />} onClick={handleSearch}>
            {t('logs.search')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={handleReset}>
            {t('logs.reset')}
          </Button>
        </Space>

        <Table
          dataSource={data?.items ?? []}
          columns={columns}
          rowKey="id"
          loading={loading}
          pagination={{
            current: page,
            pageSize: 20,
            total: data?.total ?? 0,
            onChange: setPage,
            showSizeChanger: false,
            showTotal: (total) => `${t('logs.total')} ${total} ${t('logs.logs')}`,
          }}
          size="middle"
        />
      </Card>
    </div>
  );
}

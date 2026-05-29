import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Table, Tag, Typography, Space, Input, Select, DatePicker,
  Button, Descriptions, Modal
} from 'antd';
import {
  FileTextOutlined, SearchOutlined, ReloadOutlined
} from '@ant-design/icons';
import { importsApi } from '../../api/imports';
import type { ImportLog, PagedResult } from '../../types';
import dayjs from 'dayjs';

const { Title } = Typography;
const { RangePicker } = DatePicker;

export default function ImportLogsPage() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<PagedResult<ImportLog> | null>(null);
  const [page, setPage] = useState(1);
  const [searchTable, setSearchTable] = useState('');
  const [status, setStatus] = useState<string | undefined>();
  const [dateRange, setDateRange] = useState<[dayjs.Dayjs | null, dayjs.Dayjs | null] | null>(null);
  const [detailVisible, setDetailVisible] = useState(false);
  const [selectedLog, setSelectedLog] = useState<ImportLog | null>(null);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const params: Record<string, unknown> = { page, pageSize: 20 };
      if (searchTable) params.tableName = searchTable;
      if (status) params.status = status;
      if (dateRange?.[0]) params.from = dateRange[0].toISOString();
      if (dateRange?.[1]) params.to = dateRange[1].toISOString();
      const res = await importsApi.getLogs(params as Parameters<typeof importsApi.getLogs>[0]);
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

  const columns = [
    {
      title: t('logs.id'),
      dataIndex: 'id',
      key: 'id',
      width: 60,
    },
    {
      title: t('logs.fileName'),
      dataIndex: 'fileName',
      key: 'fileName',
      ellipsis: true,
    },
    {
      title: t('logs.targetTable'),
      dataIndex: 'targetTable',
      key: 'targetTable',
    },
    {
      title: t('logs.server'),
      dataIndex: 'serverName',
      key: 'serverName',
      render: (v: string | undefined) => v || t('logs.local'),
    },
    {
      title: t('logs.user'),
      dataIndex: 'userName',
      key: 'userName',
    },
    {
      title: t('logs.status'),
      dataIndex: 'status',
      key: 'status',
      render: (s: string) => {
        const colorMap: Record<string, string> = {
          Success: 'green',
          Partial: 'orange',
          Failed: 'red',
        };
        const labelMap: Record<string, string> = {
          Success: t('logs.statusSuccess'),
          Partial: t('logs.statusPartial'),
          Failed: t('logs.statusFailed'),
        };
        return <Tag color={colorMap[s] || 'default'}>{labelMap[s] || s}</Tag>;
      },
    },
    {
      title: t('import.totalRows'),
      dataIndex: 'totalRows',
      key: 'totalRows',
      width: 80,
    },
    {
      title: t('logs.success'),
      dataIndex: 'successRows',
      key: 'successRows',
      width: 80,
      render: (v: number) => <span style={{ color: '#52c41a' }}>{v}</span>,
    },
    {
      title: t('logs.failed'),
      dataIndex: 'failedRows',
      key: 'failedRows',
      width: 80,
      render: (v: number) => <span style={{ color: '#ff4d4f' }}>{v}</span>,
    },
    {
      title: t('logs.date'),
      dataIndex: 'importedAt',
      key: 'importedAt',
      width: 180,
      render: (v: string) => dayjs(v + 'Z').format('YYYY-MM-DD HH:mm:ss'),
    },
    {
      title: t('logs.actions'),
      key: 'actions',
      width: 80,
      render: (_: unknown, record: ImportLog) => (
        <Button
          type="link"
          size="small"
          onClick={() => {
            setSelectedLog(record);
            setDetailVisible(true);
          }}
        >
          {t('logs.detail')}
        </Button>
      ),
    },
  ];

  return (
    <div>
      <Title level={4}>
        <FileTextOutlined style={{ marginRight: 8 }} />
        {t('logs.pageTitle')}
      </Title>

      <Card style={{ marginBottom: 16 }}>
        <Space wrap>
          <Input
            placeholder={t('logs.searchTable')}
            prefix={<SearchOutlined />}
            value={searchTable}
            onChange={(e) => setSearchTable(e.target.value)}
            style={{ width: 200 }}
            allowClear
          />
          <Select
            placeholder={t('logs.status')}
            value={status}
            onChange={setStatus}
            allowClear
            style={{ width: 120 }}
            options={[
              { label: t('logs.statusSuccess'), value: 'Success' },
              { label: t('logs.statusPartial'), value: 'Partial' },
              { label: t('logs.statusFailed'), value: 'Failed' },
            ]}
          />
          <RangePicker
            value={dateRange as [dayjs.Dayjs | null, dayjs.Dayjs | null] | null}
            onChange={(dates) => setDateRange(dates as [dayjs.Dayjs | null, dayjs.Dayjs | null] | null)}
          />
          <Button type="primary" icon={<SearchOutlined />} onClick={fetchLogs}>
            {t('logs.search')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={fetchLogs}>
            {t('logs.reset')}
          </Button>
        </Space>
      </Card>

      <Card>
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

      <Modal
        title={t('logs.importDetail')}
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        footer={null}
        width={600}
      >
        {selectedLog && (
          <Descriptions bordered column={2} size="small">
            <Descriptions.Item label={t('logs.id')}>{selectedLog.id}</Descriptions.Item>
            <Descriptions.Item label={t('logs.status')}>
              <Tag
                color={
                  selectedLog.status === 'Success'
                    ? 'green'
                    : selectedLog.status === 'Partial'
                    ? 'orange'
                    : 'red'
                }
              >
                {selectedLog.status}
              </Tag>
            </Descriptions.Item>
            <Descriptions.Item label={t('logs.fileName')} span={2}>
              {selectedLog.fileName}
            </Descriptions.Item>
            <Descriptions.Item label={t('logs.targetTable')} span={2}>
              {selectedLog.targetTable}
            </Descriptions.Item>
            <Descriptions.Item label={t('logs.server')} span={2}>
              {selectedLog.serverName || t('logs.local')}
            </Descriptions.Item>
            <Descriptions.Item label={t('logs.user')}>{selectedLog.userName}</Descriptions.Item>
            <Descriptions.Item label={t('logs.date')}>
              {dayjs(selectedLog.importedAt + 'Z').format('YYYY-MM-DD HH:mm:ss')}
            </Descriptions.Item>
            <Descriptions.Item label={t('import.totalRows')}>{selectedLog.totalRows}</Descriptions.Item>
            <Descriptions.Item label={t('logs.successRows')}>{selectedLog.successRows}</Descriptions.Item>
            <Descriptions.Item label={t('logs.failedRows')}>{selectedLog.failedRows}</Descriptions.Item>
            {selectedLog.errorMessage && (
              <Descriptions.Item label={t('logs.error')} span={2}>
                <Tag color="red">{selectedLog.errorMessage}</Tag>
              </Descriptions.Item>
            )}
          </Descriptions>
        )}
      </Modal>
    </div>
  );
}

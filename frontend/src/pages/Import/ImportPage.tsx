import { useState, useRef, useCallback, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  Card, Form, Select, Upload, Button, Switch, InputNumber, Table,
  message, Typography, Space, Steps, Alert, Divider, Tag, Descriptions, Progress
} from 'antd';
import {
  FileExcelOutlined, TableOutlined,
  ArrowLeftOutlined, ArrowRightOutlined, CheckCircleOutlined,
  LoadingOutlined, InboxOutlined, CloseCircleOutlined
} from '@ant-design/icons';
import type { UploadFile } from 'antd/es/upload';
import { tablesApi } from '../../api/tables';
import { importsApi } from '../../api/imports';
import { serversApi } from '../../api/servers';
import type { TableInfo, DatabaseInfo, ImportPreview, ColumnMapping, ImportResult, ImportProgress as ImportProgressType, SqlServerInstance } from '../../types';

const { Title, Text } = Typography;
const { Dragger } = Upload;

export default function ImportPage() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const [currentStep, setCurrentStep] = useState(0);
  const [servers, setServers] = useState<SqlServerInstance[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number | undefined>(undefined);
  const [tables, setTables] = useState<TableInfo[]>([]);
  const [databases, setDatabases] = useState<DatabaseInfo[]>([]);
  const [selectedDatabase, setSelectedDatabase] = useState<string>('');
  const [loadingDatabases, setLoadingDatabases] = useState(false);
  const [loadingTables, setLoadingTables] = useState(false);
  const [selectedTable, setSelectedTable] = useState<string>('');
  const [tableInfo, setTableInfo] = useState<TableInfo | null>(null);
  const [fileList, setFileList] = useState<UploadFile[]>([]);
  const [useTransaction, setUseTransaction] = useState(true);
  const [hasHeaderRow, setHasHeaderRow] = useState(true);
  const [batchSize, setBatchSize] = useState(1000);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mappings, setMappings] = useState<ColumnMapping[]>([]);
  const [importProgress, setImportProgress] = useState<ImportProgressType | null>(null);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Cleanup polling on unmount
  useEffect(() => {
    return () => {
      if (pollingRef.current) clearInterval(pollingRef.current);
    };
  }, []);

  const loadDatabases = async () => {
    setLoadingDatabases(true);
    try {
      const res = await tablesApi.getDatabases();
      setDatabases(res.data.data ?? []);
    } catch (err: unknown) {
      const detail = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        || (err as { message?: string })?.message
        || 'Unknown error';
      message.error('Failed to load databases: ' + detail);
    } finally {
      setLoadingDatabases(false);
    }
  };

  const parseDbKey = (key: string): { name: string; serverId?: number } => {
    const idx = key.indexOf('::');
    if (idx === -1) return { name: key };
    const sidStr = key.substring(0, idx);
    const sid = parseInt(sidStr, 10);
    const resolved = (isNaN(sid) || sid === 0) ? undefined : sid;
    return { name: key.substring(idx + 2), serverId: resolved };
  };

  const handleDatabaseSelect = async (key: string) => {
    const { name: database, serverId } = parseDbKey(key);
    setSelectedDatabase(database);
    setSelectedServerId(serverId);
    setSelectedTable('');
    setTableInfo(null);
    setLoadingTables(true);
    try {
      const res = await tablesApi.getAll(database, undefined, serverId);
      setTables(res.data.data ?? []);
    } catch (err: unknown) {
      const detail = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        || (err as { message?: string })?.message
        || 'Unknown error';
      message.error('Failed to load tables: ' + detail);
    } finally {
      setLoadingTables(false);
    }
  };

  const handleTableSelect = async (tableName: string) => {
    setSelectedTable(tableName);
    try {
      const res = await tablesApi.getOne(tableName, selectedDatabase, 'dbo', selectedServerId);
      setTableInfo(res.data.data ?? null);
    } catch (err: unknown) {
      const detail = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        || (err as { message?: string })?.message
        || 'Unknown error';
      message.error('Failed to load table info: ' + detail);
    }
  };

  const handlePreview = async () => {
    if (fileList.length === 0) {
      message.error(t('import.noFile'));
      return;
    }

    const formData = new FormData();
    formData.append('database', selectedDatabase);
    formData.append('tableName', selectedTable);
    formData.append('schema', 'dbo');
    formData.append('file', fileList[0] as unknown as Blob);
    formData.append('useTransaction', String(useTransaction));
    formData.append('hasHeaderRow', String(hasHeaderRow));
    formData.append('batchSize', String(batchSize));
    if (selectedServerId) formData.append('serverId', String(selectedServerId));

    try {
      const res = await importsApi.preview(formData);
      const previewData = res.data.data!;
      setPreview(previewData);
      setMappings(previewData.autoMappings);
      setCurrentStep(1);
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: Record<string, unknown>; status?: number }; message?: string };
      const detail = axiosErr.response?.data as Record<string, unknown> | undefined;
      const msg =
        (detail?.message as string) ||
        (detail?.title as string) ||
        axiosErr.message ||
        t('import.previewFailed');
      message.error(msg);
    }
  };

  const handleMappingChange = (excelColumn: string, tableColumn: string) => {
    setMappings((prev) => {
      const existing = prev.find((m) => m.excelColumn === excelColumn);
      if (existing) {
        return prev.map((m) =>
          m.excelColumn === excelColumn ? { ...m, tableColumn } : m
        );
      }
      return [...prev, { excelColumn, tableColumn }];
    });
  };

  const pollProgress = useCallback((taskId: string) => {
    // Fire immediately, then poll
    const poll = async () => {
      try {
        const res = await importsApi.getProgress(taskId);
        const prog = res.data.data!;
        setImportProgress(prog);

        if (prog.status === 'completed' || prog.status === 'failed') {
          if (pollingRef.current) clearInterval(pollingRef.current);
          pollingRef.current = null;
          if (prog.result) {
            setImportResult(prog.result);
            if (prog.result.success) {
              message.success(prog.result.message || t('import.importSuccessful'));
            } else {
              message.warning(prog.result.message || t('import.importFailed'));
            }
          }
        }
      } catch {
        // polling error, ignore
      }
    };
    poll();
    pollingRef.current = setInterval(poll, 500);
  }, [t]);

  const handleExecute = async () => {
    setImportProgress(null);
    setImportResult(null);

    const formData = new FormData();
    formData.append('database', selectedDatabase);
    formData.append('tableName', selectedTable);
    formData.append('schema', 'dbo');
    formData.append('file', fileList[0] as unknown as Blob);
    formData.append('useTransaction', String(useTransaction));
    formData.append('hasHeaderRow', String(hasHeaderRow));
    formData.append('batchSize', String(batchSize));
    formData.append('mappings', JSON.stringify(mappings.filter((m) => m.tableColumn)));
    if (selectedServerId) formData.append('serverId', String(selectedServerId));

    try {
      const res = await importsApi.execute(formData);
      const taskId = res.data.data!.taskId;
      // Show progress step immediately with "starting" state
      setImportProgress({
        taskId,
        status: 'pending',
        percent: 0,
        totalRows: 0,
        processedRows: 0,
        message: t('import.starting'),
        errorCount: 0,
      });
      setCurrentStep(2);
      pollProgress(taskId);
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: Record<string, unknown> }; message?: string };
      const detail = axiosErr.response?.data as Record<string, unknown> | undefined;
      const msg =
        (detail?.message as string) ||
        (detail?.title as string) ||
        axiosErr.message ||
        t('import.importFailedMsg');
      message.error(msg);
    }
  };

  const resetAll = () => {
    if (pollingRef.current) clearInterval(pollingRef.current);
    setCurrentStep(0);
    setSelectedServerId(undefined);
    setServers([]);
    setSelectedDatabase('');
    setDatabases([]);
    setSelectedTable('');
    setTableInfo(null);
    setFileList([]);
    setPreview(null);
    setMappings([]);
    setImportProgress(null);
    setImportResult(null);
  };

  const columns = preview?.tableColumns ?? [];
  const excelColumns = preview?.excelColumns ?? [];
  const unmappedExcelCols = excelColumns.filter(
    (ec) => !mappings.some((m) => m.excelColumn === ec) || !mappings.find((m) => m.excelColumn === ec)?.tableColumn
  );

  return (
    <div>
      <Title level={4}>
        <FileExcelOutlined style={{ marginRight: 8 }} />
        {t('import.pageTitle')}
      </Title>

      <Steps
        current={currentStep}
        style={{ marginBottom: 32 }}
        items={[
          { title: t('import.stepConfigure'), icon: <TableOutlined /> },
          { title: t('import.stepMapColumns'), icon: <FileExcelOutlined /> },
          { title: t('import.stepResult'), icon: <CheckCircleOutlined /> },
        ]}
      />

      {/* Step 0: Configure */}
      {currentStep === 0 && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Card title={t('import.selectTable')}>
            <Form layout="vertical">
              <Form.Item label="Server Instance">
                <Select
                  showSearch
                  placeholder="All servers (default: local)"
                  style={{ width: '100%' }}
                  allowClear
                  value={selectedServerId}
                  onFocus={async () => {
                    try {
                      const res = await serversApi.getAll();
                      setServers(res.data.data ?? []);
                    } catch { /* ignore */ }
                  }}
                  onChange={(val) => {
                    setSelectedServerId(val);
                    setSelectedDatabase('');
                    setSelectedTable('');
                    setTableInfo(null);
                    setDatabases([]);
                    setTables([]);
                  }}
                  optionFilterProp="label"
                  options={servers.map((s) => ({ label: s.name, value: s.id }))}
                />
              </Form.Item>
              <Form.Item label={t('import.selectDatabase')} required>
                <Select
                  showSearch
                  placeholder={t('import.selectDatabasePlaceholder')}
                  style={{ width: '100%' }}
                  value={selectedDatabase ? `${selectedServerId ?? 0}::${selectedDatabase}` : undefined}
                  onFocus={loadDatabases}
                  onChange={handleDatabaseSelect}
                  loading={loadingDatabases}
                  optionFilterProp="label"
                  options={databases.map((d) => ({
                    label: d.serverName ? `[${d.serverName}] ${d.name}` : d.name,
                    value: `${d.serverId ?? 0}::${d.name}`,
                  }))}
                />
              </Form.Item>
              <Form.Item label={t('import.selectTable')} required>
                <Select
                  showSearch
                  placeholder={selectedDatabase ? t('import.selectTablePlaceholder') : t('import.selectTableFirst')}
                  style={{ width: '100%' }}
                  value={selectedTable || undefined}
                  disabled={!selectedDatabase}
                  onChange={handleTableSelect}
                  loading={loadingTables}
                  optionFilterProp="label"
                  options={tables.map((tbl) => ({
                    label: `${tbl.schema}.${tbl.tableName}`,
                    value: tbl.tableName,
                  }))}
                />
              </Form.Item>
            </Form>
            {tableInfo && (
              <>
                <Divider />
                <Text strong>{t('import.column')}:</Text>
                <Table
                  size="small"
                  dataSource={tableInfo.columns}
                  rowKey="columnName"
                  pagination={false}
                  columns={[
                    { title: t('import.column'), dataIndex: 'columnName', key: 'columnName' },
                    { title: t('import.type'), dataIndex: 'dataType', key: 'dataType' },
                    {
                      title: t('import.nullable'),
                      dataIndex: 'isNullable',
                      key: 'isNullable',
                      render: (v: boolean) => v ? <Tag color="green">{t('import.yes')}</Tag> : <Tag color="red">{t('import.no')}</Tag>,
                    },
                    {
                      title: t('import.pk'),
                      dataIndex: 'isPrimaryKey',
                      key: 'isPrimaryKey',
                      render: (v: boolean) => v ? <Tag color="blue">{t('import.yes')}</Tag> : '-',
                    },
                    {
                      title: t('import.identity'),
                      dataIndex: 'isIdentity',
                      key: 'isIdentity',
                      render: (v: boolean) => v ? <Tag color="orange">{t('import.auto')}</Tag> : '-',
                    },
                  ]}
                  style={{ marginTop: 8 }}
                />
              </>
            )}
          </Card>

          <Card title={t('import.uploadExcel')}>
            <Form layout="vertical">
              <Form.Item label={t('import.uploadLabel')}>
                <Dragger
                  fileList={fileList}
                  accept=".xlsx,.xls,.csv"
                  beforeUpload={(file) => {
                    setFileList([file]);
                    return false;
                  }}
                  onRemove={() => setFileList([])}
                  maxCount={1}
                >
                  <p className="ant-upload-drag-icon">
                    <InboxOutlined />
                  </p>
                  <p className="ant-upload-text">{t('import.uploadText')}</p>
                  <p className="ant-upload-hint">{t('import.uploadHint')}</p>
                </Dragger>
              </Form.Item>
              <Space size="large">
                <Form.Item label={t('import.hasHeaderRow')} style={{ marginBottom: 0 }}>
                  <Switch checked={hasHeaderRow} onChange={setHasHeaderRow} />
                </Form.Item>
                <Form.Item label={t('import.useTransaction')} style={{ marginBottom: 0 }}>
                  <Switch checked={useTransaction} onChange={setUseTransaction} />
                </Form.Item>
                <Form.Item label={t('import.batchSize')} style={{ marginBottom: 0 }}>
                  <InputNumber min={100} max={10000} step={100} value={batchSize} onChange={(v) => setBatchSize(v ?? 1000)} />
                </Form.Item>
              </Space>
            </Form>
          </Card>

          <div style={{ textAlign: 'right' }}>
            <Button
              type="primary"
              size="large"
              icon={<ArrowRightOutlined />}
              disabled={!selectedTable || fileList.length === 0}
              onClick={handlePreview}
            >
              {t('import.previewNext')}
            </Button>
          </div>
        </Space>
      )}

      {/* Step 1: Map Columns */}
      {currentStep === 1 && preview && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Card title={t('import.columnMapping')}>
            {excelColumns.length === 0 ? (
              <Alert
                message={t('import.noColumnsFound')}
                type="error"
                style={{ marginBottom: 16 }}
              />
            ) : (
              <>
                <Table
                  dataSource={excelColumns.map((ec) => ({
                    excelColumn: ec,
                    tableColumn:
                      mappings.find((m) => m.excelColumn === ec)?.tableColumn ?? '',
                  }))}
                  rowKey="excelColumn"
                  pagination={false}
                  columns={[
                    { title: t('import.excelColumn'), dataIndex: 'excelColumn', key: 'excelColumn', width: 200 },
                    {
                      title: t('import.mapTo'),
                      dataIndex: 'tableColumn',
                      key: 'tableColumn',
                      render: (value: string, record: { excelColumn: string }) => (
                        <Select
                          style={{ width: 250 }}
                          placeholder={t('import.skip')}
                          allowClear
                          value={value || undefined}
                          onChange={(val) =>
                            handleMappingChange(record.excelColumn, val ?? '')
                          }
                          options={columns
                            .filter((c) => !c.isIdentity)
                            .map((c) => ({
                              label: `${c.columnName} (${c.dataType})`,
                              value: c.columnName,
                            }))}
                        />
                      ),
                    },
                    {
                      title: t('import.sampleData'),
                      key: 'sample',
                      width: 300,
                      render: (_: unknown, record: { excelColumn: string }) => (
                        <div style={{ maxHeight: 60, overflow: 'hidden' }}>
                          {preview.sampleData.slice(0, 3).map((row, i) => (
                            <div key={i} style={{ fontSize: 12, color: '#666' }}>
                              {row[record.excelColumn] || '(empty)'}
                            </div>
                          ))}
                        </div>
                      ),
                    },
                  ]}
                />
                <Alert
                  message={
                    unmappedExcelCols.length > 0
                      ? `${unmappedExcelCols.length} ${t('import.unmappedWarning')}`
                      : t('import.allMapped')
                  }
                  type={unmappedExcelCols.length > 0 ? 'warning' : 'success'}
                  style={{ marginTop: 16 }}
                />
              </>
            )}
          </Card>

          <Card title={t('import.importSummary')}>
            <Descriptions column={3} size="small">
              <Descriptions.Item label={t('import.targetTable')}>{selectedTable}</Descriptions.Item>
              <Descriptions.Item label={t('import.file')}>{fileList[0]?.name}</Descriptions.Item>
              <Descriptions.Item label={t('import.totalRows')}>{preview.totalRows}</Descriptions.Item>
              <Descriptions.Item label={t('import.transaction')}>{useTransaction ? t('import.yes') : t('import.no')}</Descriptions.Item>
              <Descriptions.Item label={t('import.headerRow')}>{hasHeaderRow ? t('import.yes') : t('import.no')}</Descriptions.Item>
              <Descriptions.Item label={t('import.mappedColumns')}>
                {mappings.filter((m) => m.tableColumn).length} / {excelColumns.length}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <Button icon={<ArrowLeftOutlined />} onClick={() => setCurrentStep(0)}>
              {t('import.back')}
            </Button>
            <Button
              type="primary"
              size="large"
              icon={<CheckCircleOutlined />}
              onClick={handleExecute}
            >
              {t('import.executeImport')}
            </Button>
          </div>
        </Space>
      )}

      {/* Step 2: Progress / Result */}
      {currentStep === 2 && (
        <Card>
          {/* Progress bar (shown while importing) */}
          {importProgress && importProgress.status !== 'completed' && importProgress.status !== 'failed' && (
            <div style={{ textAlign: 'center', padding: '24px 0' }}>
              <Title level={4}>
                <LoadingOutlined style={{ marginRight: 8 }} />
                {t('import.importing')}
              </Title>
              <Progress
                type="circle"
                percent={Math.round(importProgress.percent)}
                status="active"
                style={{ margin: '24px 0' }}
              />
              <div>
                <Text type="secondary">{importProgress.message}</Text>
              </div>
              {importProgress.totalRows > 0 && (
                <div style={{ marginTop: 8 }}>
                  <Text type="secondary">
                    {importProgress.processedRows.toLocaleString()} / {importProgress.totalRows.toLocaleString()} rows
                  </Text>
                </div>
              )}
            </div>
          )}

          {/* Result (shown when complete) */}
          {importResult && (
            <>
              <div style={{ textAlign: 'center', padding: '24px 0' }}>
                {importResult.success ? (
                  <CheckCircleOutlined style={{ fontSize: 64, color: '#52c41a' }} />
                ) : (
                  <CloseCircleOutlined style={{ fontSize: 64, color: '#ff4d4f' }} />
                )}
                <Title level={4} style={{ marginTop: 16 }}>
                  {importResult.success ? t('import.importSuccessful') : t('import.importFailed')}
                </Title>
                <Text>{importResult.message}</Text>
              </div>
              <Divider />
              <Descriptions bordered column={3}>
                <Descriptions.Item label={t('import.totalRows')}>{importResult.totalRows}</Descriptions.Item>
                <Descriptions.Item label={t('import.importSuccessful')}>
                  <Tag color="green">{importResult.importedRows}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label={t('import.importFailed')}>
                  <Tag color="red">{importResult.failedRows}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label={t('import.targetTable')}>{selectedTable}</Descriptions.Item>
                <Descriptions.Item label={t('import.file')}>{fileList[0]?.name}</Descriptions.Item>
                <Descriptions.Item label={t('import.transaction')}>{useTransaction ? t('import.yes') : t('import.no')}</Descriptions.Item>
              </Descriptions>
              {importResult.errors.length > 0 && (
                <>
                  <Divider />
                  <Title level={5}>{t('logs.error')}</Title>
                  <div style={{ maxHeight: 200, overflow: 'auto' }}>
                    {importResult.errors.map((err, i) => (
                      <Alert key={i} message={err} type="error" showIcon style={{ marginBottom: 4 }} />
                    ))}
                  </div>
                </>
              )}
              <Divider />
              <div style={{ textAlign: 'center', display: 'flex', justifyContent: 'center', gap: 12 }}>
                <Button size="large" onClick={() => navigate('/import-logs')}>
                  View Import Logs
                </Button>
                <Button type="primary" size="large" onClick={resetAll}>
                  Import Another File
                </Button>
              </div>
            </>
          )}

          {/* Failed without result */}
          {importProgress?.status === 'failed' && !importResult && (
            <div style={{ textAlign: 'center', padding: '24px 0' }}>
              <CloseCircleOutlined style={{ fontSize: 64, color: '#ff4d4f' }} />
              <Title level={4} style={{ marginTop: 16 }}>{t('import.importFailed')}</Title>
              <Text type="danger">{importProgress.message}</Text>
              <Divider />
              <Button type="primary" size="large" onClick={resetAll}>
                Import Another File
              </Button>
            </div>
          )}
        </Card>
      )}
    </div>
  );
}

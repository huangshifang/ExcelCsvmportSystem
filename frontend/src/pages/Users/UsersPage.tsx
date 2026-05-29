import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Table, Tag, Typography, Space, Button, Modal, Form, Input,
  Select, message, Popconfirm, Switch, List, Spin, Checkbox
} from 'antd';
import {
  UserOutlined, PlusOutlined, EditOutlined, DeleteOutlined,
  LockOutlined, LinkOutlined, DisconnectOutlined, SearchOutlined,
  DatabaseOutlined
} from '@ant-design/icons';
import { authApi } from '../../api/auth';
import { databaseAccessApi } from '../../api/databaseAccess';
import { serversApi } from '../../api/servers';
import { useAuth } from '../../context/AuthContext';
import type { UserInfo, Role, PagedResult, LdapSearchResult, DatabaseInfo, TableInfo, UserTableAccess, SqlServerInstance } from '../../types';

const { Title, Text } = Typography;

export default function UsersPage() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<PagedResult<UserInfo> | null>(null);
  const [page, setPage] = useState(1);
  const [modalVisible, setModalVisible] = useState(false);
  const [editingUser, setEditingUser] = useState<UserInfo | null>(null);
  const [roles] = useState<Role[]>([
    { id: 1, name: 'Admin', description: 'Full access' },
    { id: 2, name: 'Operator', description: 'Can import data' },
    { id: 3, name: 'Viewer', description: 'Read-only access' },
  ]);
  const [form] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);

  // LDAP link states
  const [ldapModalVisible, setLdapModalVisible] = useState(false);
  const [ldapLinking, setLdapLinking] = useState(false);
  const [ldapSearch, setLdapSearch] = useState('');
  const [ldapResults, setLdapResults] = useState<LdapSearchResult[]>([]);
  const [ldapSearching, setLdapSearching] = useState(false);
  const [linkTargetUser, setLinkTargetUser] = useState<UserInfo | null>(null);

  // Database access states
  const { hasPermission } = useAuth();
  const [dbAccessModalVisible, setDbAccessModalVisible] = useState(false);
  const [dbAccessUser, setDbAccessUser] = useState<UserInfo | null>(null);
  const [allDatabases, setAllDatabases] = useState<DatabaseInfo[]>([]);
  const [allServers, setAllServers] = useState<SqlServerInstance[]>([]);
  const [userAccesses, setUserAccesses] = useState<UserTableAccess[]>([]);
  const [dbTableMap, setDbTableMap] = useState<Map<string, TableInfo[]>>(new Map());
  const [expandedDbs, setExpandedDbs] = useState<Set<string>>(new Set());
  const [dbAccessLoading, setDbAccessLoading] = useState(false);

  // Reset password states
  const [resetPwdVisible, setResetPwdVisible] = useState(false);
  const [resetPwdUser, setResetPwdUser] = useState<UserInfo | null>(null);
  const [resetPwdForm] = Form.useForm();
  const [resetPwdSubmitting, setResetPwdSubmitting] = useState(false);

  const fetchUsers = async () => {
    setLoading(true);
    try {
      const res = await authApi.getUsers({ page, pageSize: 20 });
      setData(res.data.data ?? null);
    } catch {
      // handled by interceptor
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUsers();
  }, [page]);

  const handleCreate = () => {
    setEditingUser(null);
    form.resetFields();
    setModalVisible(true);
  };

  const handleEdit = (user: UserInfo) => {
    setEditingUser(user);
    form.setFieldsValue({
      displayName: user.displayName,
      email: user.email,
      isActive: user.isActive,
    });
    setModalVisible(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      if (editingUser) {
        await authApi.updateUser(editingUser.id, values);
        message.success('User updated');
      } else {
        await authApi.createUser(values);
        message.success('User created');
      }

      setModalVisible(false);
      fetchUsers();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        'Operation failed';
      message.error(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await authApi.deleteUser(id);
      message.success('User deleted');
      fetchUsers();
    } catch {
      message.error('Failed to delete user');
    }
  };

  const handleLinkLdap = (user: UserInfo) => {
    setLinkTargetUser(user);
    setLdapSearch('');
    setLdapResults([]);
    setLdapModalVisible(true);
  };

  const handleLdapSearch = async (searchVal?: string) => {
    const term = searchVal ?? ldapSearch;
    if (!term.trim()) {
      setLdapResults([]);
      return;
    }
    setLdapSearching(true);
    try {
      const res = await authApi.searchLdapUsers(term);
      setLdapResults(res.data.data ?? []);
    } catch {
      message.error('AD search failed');
    } finally {
      setLdapSearching(false);
    }
  };

  const handleConfirmLink = async (ldapUser: LdapSearchResult) => {
    if (!linkTargetUser) return;
    setLdapLinking(true);
    try {
      await authApi.linkLdap(linkTargetUser.id, { username: ldapUser.samAccountName });
      message.success(t('users.linkLdapSuccess'));
      setLdapModalVisible(false);
      fetchUsers();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        'Failed to link AD account';
      message.error(msg);
    } finally {
      setLdapLinking(false);
    }
  };

  const handleUnlinkLdap = async (userId: number) => {
    try {
      await authApi.unlinkLdap(userId);
      message.success(t('users.unlinkLdapSuccess'));
      fetchUsers();
    } catch {
      message.error('Failed to unlink AD account');
    }
  };

  const handleOpenDbAccess = async (user: UserInfo) => {
    setDbAccessUser(user);
    setDbAccessModalVisible(true);
    setDbAccessLoading(true);
    setDbTableMap(new Map());
    setExpandedDbs(new Set());
    try {
      const [allDbRes, userAccessRes, serversRes] = await Promise.all([
        databaseAccessApi.getAvailableDatabases(),
        databaseAccessApi.getUserTableAccesses(user.id),
        serversApi.getAll(),
      ]);
      setAllDatabases(allDbRes.data.data ?? []);
      setUserAccesses(userAccessRes.data.data ?? []);
      setAllServers(serversRes.data.data ?? []);
    } catch {
      message.error('Failed to load database access info');
    } finally {
      setDbAccessLoading(false);
    }
  };

  const handleExpandDatabase = async (database: string, serverId?: number) => {
    const mapKey = `${serverId ?? 0}:${database}`;
    if (dbTableMap.has(mapKey)) return;
    try {
      const res = await databaseAccessApi.getDatabaseTables(database, serverId);
      setDbTableMap(prev => new Map(prev).set(mapKey, res.data.data ?? []));
      setExpandedDbs(prev => new Set(prev).add(mapKey));
    } catch {
      message.error(`Failed to load tables for ${database}`);
    }
  };

  const isDatabaseWildcard = (database: string, serverId?: number) =>
    userAccesses.some(a => a.databaseName === database && !a.tableName && (a.serverId ?? undefined) === (serverId ?? undefined));

  const isTableGranted = (database: string, schema: string, table: string, serverId?: number) =>
    userAccesses.some(a =>
      a.databaseName === database && a.tableName === table && (a.schemaName || 'dbo') === schema && (a.serverId ?? undefined) === (serverId ?? undefined)
    );

  const handleToggleWildcard = (serverId: number | undefined, database: string, checked: boolean) => {
    if (checked) {
      setUserAccesses(prev => [
        ...prev.filter(a => !(a.databaseName === database && (a.serverId ?? undefined) === (serverId ?? undefined))),
        { serverId, databaseName: database },
      ]);
    } else {
      setUserAccesses(prev => prev.filter(a => !(a.databaseName === database && (a.serverId ?? undefined) === (serverId ?? undefined))));
    }
  };

  const handleToggleTable = (serverId: number | undefined, database: string, schema: string, table: string, checked: boolean) => {
    if (checked) {
      setUserAccesses(prev => [...prev, {
        serverId,
        databaseName: database,
        schemaName: schema,
        tableName: table,
      }]);
    } else {
      setUserAccesses(prev => prev.filter(a =>
        !(a.databaseName === database
          && a.tableName === table
          && (a.schemaName || 'dbo') === schema
          && (a.serverId ?? undefined) === (serverId ?? undefined))
      ));
    }
  };

  const handleSaveDbAccess = async () => {
    if (!dbAccessUser) return;
    setDbAccessLoading(true);
    try {
      await databaseAccessApi.setUserTableAccesses(dbAccessUser.id, userAccesses);
      message.success(t('users.databaseAccessSuccess'));
      setDbAccessModalVisible(false);
    } catch {
      message.error('Failed to save database access');
    } finally {
      setDbAccessLoading(false);
    }
  };

  const handleOpenResetPwd = (user: UserInfo) => {
    setResetPwdUser(user);
    resetPwdForm.resetFields();
    setResetPwdVisible(true);
  };

  const handleResetPwdSubmit = async () => {
    if (!resetPwdUser) return;
    try {
      const values = await resetPwdForm.validateFields();
      setResetPwdSubmitting(true);
      await authApi.resetUserPassword(resetPwdUser.id, { newPassword: values.newPassword });
      message.success(t('users.resetPasswordSuccess'));
      setResetPwdVisible(false);
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        'Failed to reset password';
      message.error(msg);
    } finally {
      setResetPwdSubmitting(false);
    }
  };

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 60 },
    { title: 'Username', dataIndex: 'username', key: 'username' },
    { title: 'Display Name', dataIndex: 'displayName', key: 'displayName' },
    { title: 'Email', dataIndex: 'email', key: 'email' },
    {
      title: 'Auth',
      dataIndex: 'authType',
      key: 'authType',
      width: 70,
      render: (v: string) => (
        <Tag color={v === 'LDAP' ? 'blue' : 'default'}>
          {v === 'LDAP' ? t('users.ldap') : t('users.local')}
        </Tag>
      ),
    },
    {
      title: 'Roles',
      dataIndex: 'roles',
      key: 'roles',
      render: (roles: string[]) => (
        <Space>
          {roles.map((r) => (
            <Tag key={r} color={r === 'Admin' ? 'red' : r === 'Operator' ? 'blue' : 'default'}>
              {r}
            </Tag>
          ))}
        </Space>
      ),
    },
    {
      title: 'Status',
      dataIndex: 'isActive',
      key: 'isActive',
      render: (v: boolean) => (v ? <Tag color="green">Active</Tag> : <Tag color="red">Disabled</Tag>),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_: unknown, record: UserInfo) => (
        <Space wrap>
          <Button
            type="link"
            icon={<EditOutlined />}
            disabled={record.id === 1}
            onClick={() => handleEdit(record)}
          >
            Edit
          </Button>
          {record.authType !== 'LDAP' && (
            <Button
              type="link"
              icon={<LockOutlined />}
              onClick={() => handleOpenResetPwd(record)}
            >
              {t('users.resetPassword')}
            </Button>
          )}
          {record.authType !== 'LDAP' ? (
            <Button
              type="link"
              icon={<LinkOutlined />}
              onClick={() => handleLinkLdap(record)}
            >
              {t('users.linkLdap')}
            </Button>
          ) : (
            <Popconfirm
              title={t('users.unlinkLdapConfirm')}
              onConfirm={() => handleUnlinkLdap(record.id)}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button type="link" icon={<DisconnectOutlined />}>
                {t('users.unlinkLdap')}
              </Button>
            </Popconfirm>
          )}
          {hasPermission('Database.Manage') && (
            <Button
              type="link"
              icon={<DatabaseOutlined />}
              onClick={() => handleOpenDbAccess(record)}
            >
              {t('users.databaseAccess')}
            </Button>
          )}
          <Popconfirm
            title="Delete this user?"
            description="This action cannot be undone."
            onConfirm={() => handleDelete(record.id)}
            okText="Delete"
            cancelText="Cancel"
            okButtonProps={{ danger: true }}
          >
            <Button
              type="link"
              danger
              icon={<DeleteOutlined />}
              disabled={record.id === 1}
            >
              Delete
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Title level={4}>
        <UserOutlined style={{ marginRight: 8 }} />
        {t('users.pageTitle')}
      </Title>

      <Card>
        <div style={{ marginBottom: 16, textAlign: 'right' }}>
          <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
            {t('users.addUser')}
          </Button>
        </div>
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
            showTotal: (total) => `${t('logs.total')} ${total} ${t('users.username')}`,
          }}
          size="middle"
        />
      </Card>

      <Modal
        title={editingUser ? t('users.editUser') : t('users.addUser')}
        open={modalVisible}
        onOk={handleSubmit}
        onCancel={() => setModalVisible(false)}
        confirmLoading={submitting}
        width={500}
      >
        <Form form={form} layout="vertical">
          {!editingUser && (
            <>
              <Form.Item
                name="username"
                label={t('users.username')}
                rules={[{ required: true, message: t('users.username') }]}
              >
                <Input />
              </Form.Item>
              <Form.Item
                name="password"
                label={t('users.password')}
                rules={[{ min: 6, message: 'Password must be at least 6 characters' }]}
              >
                <Input.Password prefix={<LockOutlined />} />
              </Form.Item>
            </>
          )}
          <Form.Item
            name="displayName"
            label={t('users.displayName')}
            rules={[{ required: true, message: t('users.displayName') }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="email" label={t('users.email')}>
            <Input type="email" />
          </Form.Item>
          {editingUser && (
            <Form.Item name="isActive" label={t('users.isActive')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          {!editingUser && (
            <Form.Item name="roleIds" label={t('users.roles')}>
              <Select
                mode="multiple"
                placeholder={t('users.roles')}
                options={roles.map((r) => ({ label: r.name, value: r.id }))}
              />
            </Form.Item>
          )}
        </Form>
      </Modal>

      <Modal
        title={t('users.resetPasswordTitle', { username: resetPwdUser?.username ?? '' })}
        open={resetPwdVisible}
        onOk={handleResetPwdSubmit}
        onCancel={() => setResetPwdVisible(false)}
        confirmLoading={resetPwdSubmitting}
        destroyOnClose
      >
        <div style={{ marginBottom: 16, marginTop: 8 }}>
          <Text type="secondary">{t('users.resetPasswordHint')}</Text>
        </div>
        <Form form={resetPwdForm} layout="vertical">
          <Form.Item
            name="newPassword"
            label={t('changePassword.newPassword')}
            rules={[
              { required: true, message: t('changePassword.newPassword') },
              { min: 6, message: t('changePassword.minLength') },
            ]}
          >
            <Input.Password prefix={<LockOutlined />} />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label={t('changePassword.confirmPassword')}
            dependencies={['newPassword']}
            rules={[
              { required: true, message: t('changePassword.confirmPassword') },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('newPassword') === value) return Promise.resolve();
                  return Promise.reject(new Error(t('changePassword.passwordMismatch')));
                },
              }),
            ]}
          >
            <Input.Password prefix={<LockOutlined />} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={t('users.linkLdapTitle')}
        open={ldapModalVisible}
        onCancel={() => setLdapModalVisible(false)}
        footer={null}
        width={500}
      >
        <div style={{ marginBottom: 16 }}>
          <Text type="secondary">{t('users.linkLdapHint')}</Text>
          {linkTargetUser && (
            <Text style={{ marginLeft: 8 }} strong>
              ({linkTargetUser.username})
            </Text>
          )}
        </div>
        <Input.Search
          placeholder={t('users.ldapSearchPlaceholder')}
          value={ldapSearch}
          onChange={(e) => setLdapSearch(e.target.value)}
          onSearch={(v) => handleLdapSearch(v)}
          enterButton={<SearchOutlined />}
          loading={ldapSearching}
          style={{ marginBottom: 16 }}
        />
        <Spin spinning={ldapLinking}>
          <List
            dataSource={ldapResults}
            locale={{ emptyText: t('users.ldapSearchNoResults') }}
            renderItem={(item) => (
              <List.Item
                actions={[
                  <Button
                    type="link"
                    icon={<LinkOutlined />}
                    onClick={() => handleConfirmLink(item)}
                  >
                    {t('users.linkLdap')}
                  </Button>,
                ]}
              >
                <List.Item.Meta
                  title={item.displayName || item.samAccountName}
                  description={`${item.samAccountName}${item.email ? ` | ${item.email}` : ''}`}
                />
              </List.Item>
            )}
          />
        </Spin>
      </Modal>

      <Modal
        title={t('users.databaseAccessTitle', { username: dbAccessUser?.username ?? '' })}
        open={dbAccessModalVisible}
        onOk={handleSaveDbAccess}
        onCancel={() => setDbAccessModalVisible(false)}
        confirmLoading={dbAccessLoading}
        okText={t('users.databaseAccessSave')}
        width={600}
        destroyOnClose
      >
        <Text type="secondary" style={{ display: 'block', marginBottom: 16 }}>
          {t('users.tableAccessHint')}
        </Text>
        <Spin spinning={dbAccessLoading}>
          {allDatabases.length === 0 && !dbAccessLoading ? (
            <Text type="secondary">{t('users.noDatabaseAccess')}</Text>
          ) : (
            <div style={{ maxHeight: 400, overflow: 'auto' }}>
              {(() => {
                // Group databases by server
                const grouped = new Map<string, DatabaseInfo[]>();
                const localGroup: DatabaseInfo[] = [];
                for (const db of allDatabases) {
                  if (db.serverId != null) {
                    const key = String(db.serverId);
                    if (!grouped.has(key)) grouped.set(key, []);
                    grouped.get(key)!.push(db);
                  } else {
                    localGroup.push(db);
                  }
                }
                const getServerName = (serverId: number) =>
                  allServers.find(s => s.id === serverId)?.name || `Server #${serverId}`;

                const renderDb = (db: DatabaseInfo) => {
                  const serverId = db.serverId;
                  const mapKey = `${serverId ?? 0}:${db.name}`;
                  return (
                    <div key={mapKey} style={{ marginBottom: 12 }}>
                      <div
                        style={{ display: 'flex', alignItems: 'center', cursor: 'pointer' }}
                        onClick={() => {
                          if (!isDatabaseWildcard(db.name, serverId)) {
                            handleExpandDatabase(db.name, serverId);
                          }
                        }}
                      >
                        <Checkbox
                          checked={isDatabaseWildcard(db.name, serverId)}
                          onChange={(e) => handleToggleWildcard(serverId, db.name, e.target.checked)}
                          onClick={(e) => e.stopPropagation()}
                        >
                          <strong>{db.name}</strong> {t('users.selectAllTables')}
                        </Checkbox>
                      </div>
                      {!isDatabaseWildcard(db.name, serverId) && expandedDbs.has(mapKey) && (
                        <div style={{ marginLeft: 24, marginTop: 4 }}>
                          {(dbTableMap.get(mapKey) ?? []).map(table => (
                            <Checkbox
                              key={`${serverId ?? 0}:${table.schema}.${table.tableName}`}
                              checked={isTableGranted(db.name, table.schema, table.tableName, serverId)}
                              onChange={(e) => handleToggleTable(serverId, db.name, table.schema, table.tableName, e.target.checked)}
                              style={{ display: 'block', marginBottom: 4 }}
                            >
                              {table.schema}.{table.tableName}
                            </Checkbox>
                          ))}
                        </div>
                      )}
                    </div>
                  );
                };

                return (
                  <>
                    {localGroup.length > 0 && (
                      <div style={{ marginBottom: 8 }}>
                        <Text strong style={{ fontSize: 13, color: '#888' }}>Local Server</Text>
                        {localGroup.map(renderDb)}
                      </div>
                    )}
                    {Array.from(grouped.entries()).map(([serverIdKey, dbs]) => (
                      <div key={serverIdKey} style={{ marginBottom: 8 }}>
                        <Text strong style={{ fontSize: 13, color: '#1890ff' }}>[{getServerName(Number(serverIdKey))}]</Text>
                        {dbs.map(renderDb)}
                      </div>
                    ))}
                  </>
                );
              })()}
            </div>
          )}
        </Spin>
      </Modal>
    </div>
  );
}

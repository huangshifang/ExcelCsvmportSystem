import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Table, Tag, Typography, Space, Button, Modal, Form, Input,
  Select, message, Popconfirm, Switch, List, Spin
} from 'antd';
import {
  UserOutlined, PlusOutlined, EditOutlined, DeleteOutlined,
  LockOutlined, LinkOutlined, DisconnectOutlined, SearchOutlined
} from '@ant-design/icons';
import { authApi } from '../../api/auth';
import type { UserInfo, Role, PagedResult, LdapSearchResult } from '../../types';

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
    </div>
  );
}

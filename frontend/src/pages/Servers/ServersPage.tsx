import { useState, useEffect } from 'react';
import { Card, Table, Typography, Space, Button, Modal, Form, Input, Switch, message, Popconfirm, Tag } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, CloudServerOutlined, LinkOutlined } from '@ant-design/icons';
import { serversApi } from '../../api/servers';
import { useAuth } from '../../context/AuthContext';
import type { SqlServerInstance } from '../../types';

const { Title } = Typography;

export default function ServersPage() {
  const { hasPermission } = useAuth();
  const [loading, setLoading] = useState(false);
  const [servers, setServers] = useState<SqlServerInstance[]>([]);
  const [modalVisible, setModalVisible] = useState(false);
  const [editingServer, setEditingServer] = useState<SqlServerInstance | null>(null);
  const [testModalVisible, setTestModalVisible] = useState(false);
  const [testing, setTesting] = useState(false);
  const [form] = Form.useForm();
  const [testForm] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);

  const fetchServers = async () => {
    setLoading(true);
    try {
      const res = await serversApi.getAll();
      setServers(res.data.data ?? []);
    } catch {
      message.error('Failed to load servers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchServers(); }, []);

  const handleCreate = () => {
    setEditingServer(null);
    form.resetFields();
    setModalVisible(true);
  };

  const handleEdit = (server: SqlServerInstance) => {
    setEditingServer(server);
    form.setFieldsValue({ name: server.name, description: server.description, isActive: server.isActive });
    setModalVisible(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);
      if (editingServer) {
        const payload: Record<string, unknown> = {};
        if (values.name) payload.name = values.name;
        if (values.connectionString) payload.connectionString = values.connectionString;
        if (values.description !== undefined) payload.description = values.description;
        if (values.isActive !== undefined) payload.isActive = values.isActive;
        await serversApi.update(editingServer.id, payload);
        message.success('Server updated');
      } else {
        await serversApi.create(values);
        message.success('Server created');
      }
      setModalVisible(false);
      fetchServers();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Failed');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await serversApi.delete(id);
      message.success('Server deleted');
      fetchServers();
    } catch (err: unknown) {
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Failed to delete');
    }
  };

  const handleTestConnection = async () => {
    try {
      const values = await testForm.validateFields();
      setTesting(true);
      const res = await serversApi.test(values.connectionString);
      message.success(res.data.message || 'Connection successful');
      setTestModalVisible(false);
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      message.error((err as { response?: { data?: { message?: string } } })?.response?.data?.message || 'Connection failed');
    } finally {
      setTesting(false);
    }
  };

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 60 },
    { title: 'Name', dataIndex: 'name', key: 'name' },
    { title: 'Description', dataIndex: 'description', key: 'description', render: (v?: string) => v || '-' },
    {
      title: 'Status', dataIndex: 'isActive', key: 'isActive', width: 80,
      render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? 'Active' : 'Inactive'}</Tag>,
    },
    { title: 'Created', dataIndex: 'createdAt', key: 'createdAt', render: (v: string) => new Date(v).toLocaleDateString() },
    ...(hasPermission('Server.Manage') ? [{
      title: 'Actions', key: 'actions',
      render: (_: unknown, record: SqlServerInstance) => (
        <Space>
          <Button type="link" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Edit</Button>
          <Popconfirm title="Delete this server?" onConfirm={() => handleDelete(record.id)} okText="Delete" cancelText="Cancel" okButtonProps={{ danger: true }}>
            <Button type="link" danger icon={<DeleteOutlined />}>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    }] : []),
  ];

  return (
    <div>
      <Title level={4}><CloudServerOutlined style={{ marginRight: 8 }} />Server Instances</Title>
      <Card>
        <div style={{ marginBottom: 16, display: 'flex', gap: 8 }}>
          {hasPermission('Server.Manage') && (
            <>
              <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>Add Server</Button>
              <Button icon={<LinkOutlined />} onClick={() => { testForm.resetFields(); setTestModalVisible(true); }}>Test Connection</Button>
            </>
          )}
        </div>
        <Table dataSource={servers} columns={columns} rowKey="id" loading={loading} pagination={false} size="middle" />
      </Card>

      <Modal title={editingServer ? 'Edit Server' : 'Add Server'} open={modalVisible} onOk={handleSubmit} onCancel={() => setModalVisible(false)} confirmLoading={submitting} width={560}>
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input /></Form.Item>
          {!editingServer && (
            <Form.Item name="connectionString" label="Connection String" rules={[{ required: true }]}>
              <Input.Password placeholder="Server=host;Database=master;User Id=sa;Password=...;TrustServerCertificate=True" />
            </Form.Item>
          )}
          {editingServer && (
            <Form.Item name="connectionString" label="Connection String (leave blank to keep current)">
              <Input.Password placeholder="Leave blank to keep current" />
            </Form.Item>
          )}
          <Form.Item name="description" label="Description"><Input /></Form.Item>
          {editingServer && (
            <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>
          )}
        </Form>
      </Modal>

      <Modal title="Test Connection" open={testModalVisible} onOk={handleTestConnection} onCancel={() => setTestModalVisible(false)} confirmLoading={testing} okText="Test">
        <Form form={testForm} layout="vertical">
          <Form.Item name="connectionString" label="Connection String" rules={[{ required: true }]}>
            <Input.Password placeholder="Server=host;Database=master;User Id=sa;Password=...;TrustServerCertificate=True" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}

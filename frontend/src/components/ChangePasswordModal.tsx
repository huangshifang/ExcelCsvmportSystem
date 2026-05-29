import { useState } from 'react';
import { Modal, Form, Input, message } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { authApi } from '../api/auth';
import { useAuth } from '../context/AuthContext';

interface Props {
  open: boolean;
  onClose: () => void;
}

export default function ChangePasswordModal({ open, onClose }: Props) {
  const { t } = useTranslation();
  const { logout } = useAuth();
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      if (values.newPassword !== values.confirmPassword) {
        message.error(t('changePassword.passwordMismatch'));
        return;
      }
      setLoading(true);
      await authApi.changePassword({
        oldPassword: values.oldPassword,
        newPassword: values.newPassword,
      });
      message.success(t('changePassword.success'));
      form.resetFields();
      onClose();
      setTimeout(() => logout(), 1500);
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown })?.errorFields) return;
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        t('changePassword.failed');
      message.error(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Modal
      title={t('changePassword.title')}
      open={open}
      onOk={handleOk}
      onCancel={() => {
        form.resetFields();
        onClose();
      }}
      confirmLoading={loading}
      destroyOnClose
    >
      <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
        <Form.Item
          name="oldPassword"
          label={t('changePassword.oldPassword')}
          rules={[{ required: true, message: t('changePassword.oldPassword') }]}
        >
          <Input.Password prefix={<LockOutlined />} />
        </Form.Item>
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
          rules={[{ required: true, message: t('changePassword.confirmPassword') }]}
        >
          <Input.Password prefix={<LockOutlined />} />
        </Form.Item>
      </Form>
    </Modal>
  );
}

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, Row, Col, Statistic, Typography, Spin } from 'antd';
import {
  FileTextOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  ImportOutlined,
} from '@ant-design/icons';
import { useAuth } from '../../context/AuthContext';
import { importsApi } from '../../api/imports';
import type { DashboardStats } from '../../types';

const { Title } = Typography;

export default function DashboardPage() {
  const { user } = useAuth();
  const { t } = useTranslation();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    importsApi
      .getDashboardStats()
      .then((res) => {
        if (res.data.success && res.data.data) {
          setStats(res.data.data);
        }
      })
      .finally(() => setLoading(false));
  }, []);

  return (
    <div>
      <Title level={4} style={{ marginBottom: 24 }}>
        {t('dashboard.welcome')}, {user?.displayName}
      </Title>
      <Spin spinning={loading}>
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={12} lg={6}>
            <Card hoverable>
              <Statistic
                title={t('dashboard.totalImports')}
                value={stats?.totalImports ?? 0}
                prefix={<ImportOutlined style={{ color: '#1890ff' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={12} lg={6}>
            <Card hoverable>
              <Statistic
                title={t('dashboard.totalRows')}
                value={stats?.totalRows ?? 0}
                prefix={<FileTextOutlined style={{ color: '#722ed1' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={12} lg={6}>
            <Card hoverable>
              <Statistic
                title={t('dashboard.successRows')}
                value={stats?.successRows ?? 0}
                prefix={<CheckCircleOutlined style={{ color: '#52c41a' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={12} lg={6}>
            <Card hoverable>
              <Statistic
                title={t('dashboard.failedRows')}
                value={stats?.failedRows ?? 0}
                prefix={<CloseCircleOutlined style={{ color: '#ff4d4f' }} />}
              />
            </Card>
          </Col>
        </Row>
      </Spin>
    </div>
  );
}

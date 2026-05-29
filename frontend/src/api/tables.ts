import apiClient from './client';
import type { ApiResponse, TableInfo, DatabaseInfo } from '../types';

export const tablesApi = {
  getDatabases: () =>
    apiClient.get<ApiResponse<DatabaseInfo[]>>('/table/databases'),

  getAll: (database: string, schema?: string, serverId?: number) =>
    apiClient.get<ApiResponse<TableInfo[]>>('/table', { params: { database, schema, serverId } }),

  getOne: (tableName: string, database: string, schema = 'dbo', serverId?: number) =>
    apiClient.get<ApiResponse<TableInfo>>(`/table/${encodeURIComponent(tableName)}`, { params: { database, schema, serverId } }),
};

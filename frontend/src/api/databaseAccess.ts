import apiClient from './client';
import type { ApiResponse, DatabaseInfo, TableInfo, UserTableAccess } from '../types';

export const databaseAccessApi = {
  getAvailableDatabases: () =>
    apiClient.get<ApiResponse<DatabaseInfo[]>>('/databaseaccess/available-databases'),

  getDatabaseTables: (database: string, serverId?: number) =>
    apiClient.get<ApiResponse<TableInfo[]>>(`/databaseaccess/tables/${encodeURIComponent(database)}`, { params: serverId !== undefined ? { serverId } : {} }),

  getUserTableAccesses: (userId: number) =>
    apiClient.get<ApiResponse<UserTableAccess[]>>(`/databaseaccess/user/${userId}`),

  setUserTableAccesses: (userId: number, accesses: UserTableAccess[]) =>
    apiClient.put<ApiResponse<unknown>>(`/databaseaccess/user/${userId}`, accesses),
};

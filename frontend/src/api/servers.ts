import apiClient from './client';
import type { ApiResponse, SqlServerInstance, CreateServerRequest, UpdateServerRequest } from '../types';

export const serversApi = {
  getAll: () =>
    apiClient.get<ApiResponse<SqlServerInstance[]>>('/server'),

  getById: (id: number) =>
    apiClient.get<ApiResponse<SqlServerInstance>>(`/server/${id}`),

  create: (data: CreateServerRequest) =>
    apiClient.post<ApiResponse<SqlServerInstance>>('/server', data),

  update: (id: number, data: UpdateServerRequest) =>
    apiClient.put<ApiResponse<SqlServerInstance>>(`/server/${id}`, data),

  delete: (id: number) =>
    apiClient.delete<ApiResponse<null>>(`/server/${id}`),

  test: (connectionString: string) =>
    apiClient.post<ApiResponse<{ version: string }>>('/server/test', { connectionString }),
};

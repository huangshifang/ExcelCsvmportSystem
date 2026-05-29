import apiClient from './client';
import type { ApiResponse, ImportPreview, ImportExecuteResponse, ImportProgress, ImportLog, PagedResult, DashboardStats } from '../types';

export const importsApi = {
  preview: (formData: FormData) =>
    apiClient.post<ApiResponse<ImportPreview>>('/import/preview', formData, {
      timeout: 300000,
    }),

  execute: (formData: FormData) =>
    apiClient.post<ApiResponse<ImportExecuteResponse>>('/import/execute', formData, {
      timeout: 300000,
    }),

  getProgress: (taskId: string) =>
    apiClient.get<ApiResponse<ImportProgress>>(`/import/progress/${taskId}`),

  getLogs: (params: {
    page?: number;
    pageSize?: number;
    tableName?: string;
    status?: string;
    from?: string;
    to?: string;
  }) => apiClient.get<ApiResponse<PagedResult<ImportLog>>>('/importlog', { params }),

  getLogById: (id: number) =>
    apiClient.get<ApiResponse<ImportLog>>(`/importlog/${id}`),

  getDashboardStats: () =>
    apiClient.get<ApiResponse<DashboardStats>>('/dashboard/stats'),
};

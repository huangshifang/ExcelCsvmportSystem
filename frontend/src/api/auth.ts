import apiClient from './client';
import type { ApiResponse, LoginRequest, LoginResponse, UserInfo, PagedResult, CreateUserRequest, UpdateUserRequest, LinkLdapRequest, LdapSearchResult } from '../types';

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<ApiResponse<LoginResponse>>('/auth/login', data),

  getCurrentUser: () =>
    apiClient.get<ApiResponse<UserInfo>>('/auth/me'),

  changePassword: (data: { oldPassword: string; newPassword: string }) =>
    apiClient.post<ApiResponse<null>>('/auth/change-password', data),

  getUsers: (params: { page?: number; pageSize?: number; search?: string }) =>
    apiClient.get<ApiResponse<PagedResult<UserInfo>>>('/auth/users', { params }),

  createUser: (data: CreateUserRequest) =>
    apiClient.post<ApiResponse<UserInfo>>('/auth/users', data),

  updateUser: (id: number, data: UpdateUserRequest) =>
    apiClient.put<ApiResponse<UserInfo>>(`/auth/users/${id}`, data),

  deleteUser: (id: number) =>
    apiClient.delete<ApiResponse<null>>(`/auth/users/${id}`),

  linkLdap: (userId: number, data: LinkLdapRequest) =>
    apiClient.post<ApiResponse<UserInfo>>(`/auth/users/${userId}/link-ldap`, data),

  unlinkLdap: (userId: number) =>
    apiClient.delete<ApiResponse<null>>(`/auth/users/${userId}/unlink-ldap`),

  searchLdapUsers: (search?: string) =>
    apiClient.get<ApiResponse<LdapSearchResult[]>>('/auth/ldap-users', { params: { search } }),
};

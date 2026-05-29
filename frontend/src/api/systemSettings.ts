import apiClient from './client';
import type { ApiResponse, LdapSettings } from '../types';

export const systemSettingsApi = {
  getLdapSettings: () =>
    apiClient.get<ApiResponse<LdapSettings>>('/systemsettings/ldap'),

  updateLdapSettings: (data: LdapSettings) =>
    apiClient.put<ApiResponse<unknown>>('/systemsettings/ldap', data),

  testLdapConnection: (data: LdapSettings) =>
    apiClient.post<ApiResponse<{ success: boolean; dn?: string; displayName?: string; email?: string }>>('/systemsettings/ldap/test', data),
};

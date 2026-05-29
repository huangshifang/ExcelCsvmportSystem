export interface UserInfo {
  id: number;
  username: string;
  displayName: string;
  email: string;
  isActive: boolean;
  roles: string[];
  authType: string;
  ldapDn?: string;
}

export interface LoginResponse {
  token: string;
  displayName: string;
  roles: string[];
  permissions: string[];
}

export interface LoginRequest {
  username: string;
  password: string;
  captchaCode?: string;
  captchaToken?: string;
}

export interface ChangePasswordRequest {
  oldPassword: string;
  newPassword: string;
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface CreateUserRequest {
  username: string;
  password?: string;
  displayName: string;
  email?: string;
  roleIds: number[];
  authType?: string;
  ldapDn?: string;
}

export interface UpdateUserRequest {
  displayName?: string;
  email?: string;
  isActive?: boolean;
  roleIds?: number[];
  authType?: string;
  ldapDn?: string;
}

export interface LinkLdapRequest {
  username: string;
}

export interface LdapSearchResult {
  dn: string;
  samAccountName: string;
  displayName: string;
  email: string;
}

export interface Role {
  id: number;
  name: string;
  description: string;
}

export interface RolePermission {
  id: number;
  roleId: number;
  permission: string;
}

export interface SqlServerInstance {
  id: number;
  name: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateServerRequest {
  name: string;
  connectionString: string;
  description?: string;
}

export interface UpdateServerRequest {
  name?: string;
  connectionString?: string;
  description?: string;
  isActive?: boolean;
}

export interface DatabaseInfo {
  name: string;
  serverId?: number;
  serverName?: string;
}

export interface UserTableAccess {
  serverId?: number;
  databaseName: string;
  schemaName?: string;
  tableName?: string;
}

export interface UserDatabaseAccess {
  userId: number;
  databaseName: string;
  schemaName?: string;
  tableName?: string;
  grantedBy?: string;
  grantedAt?: string;
}

export interface TableInfo {
  database: string;
  schema: string;
  tableName: string;
  comment?: string;
  columns: ColumnInfo[];
}

export interface ColumnInfo {
  columnName: string;
  dataType: string;
  isNullable: boolean;
  maxLength?: number;
  isPrimaryKey: boolean;
  isIdentity: boolean;
}

export interface ColumnMapping {
  excelColumn: string;
  tableColumn: string;
}

export interface ImportPreview {
  excelColumns: string[];
  sampleData: Record<string, string>[];
  tableColumns: ColumnInfo[];
  autoMappings: ColumnMapping[];
  totalRows: number;
}

export interface ImportRequest {
  tableName: string;
  schema: string;
  file: File;
  useTransaction: boolean;
  hasHeaderRow: boolean;
  batchSize: number;
}

export interface ImportResult {
  success: boolean;
  message: string;
  totalRows: number;
  importedRows: number;
  failedRows: number;
  errors: string[];
  importLogId?: number;
}

export interface ImportLog {
  id: number;
  userId: number;
  userName: string;
  targetTable: string;
  fileName: string;
  totalRows: number;
  successRows: number;
  failedRows: number;
  status: string;
  errorMessage?: string;
  serverId?: number;
  serverName?: string;
  importedAt: string;
}

export interface DashboardStats {
  totalImports: number;
  totalRows: number;
  successRows: number;
  failedRows: number;
}

export interface PagedResult<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

export interface LdapSettings {
  enabled: boolean;
  server: string;
  port: number;
  useSsl: boolean;
  domain: string;
  baseDn: string;
  userFilterTemplate: string;
  bindUserDn: string;
  bindPassword?: string;
  bindPasswordSet: boolean;
  testUsername?: string;
  testPassword?: string;
}

export interface LoginAuditLog {
  id: number;
  username: string;
  ipAddress?: string;
  userAgent?: string;
  success: boolean;
  failureReason?: string;
  timestamp: string;
}

export interface ImportExecuteResponse {
  taskId: string;
}

export interface ImportProgress {
  taskId: string;
  status: 'pending' | 'reading' | 'importing' | 'completed' | 'failed';
  percent: number;
  totalRows: number;
  processedRows: number;
  message: string;
  errorCount: number;
  result?: ImportResult;
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data?: T;
  errors?: string[];
}

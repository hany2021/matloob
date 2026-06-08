/** Back-office admin user models — mirror the .NET Admin DTOs (camelCase JSON). */

export interface AdminListItem {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
  identityId: string | null;
}

export interface ListAdminsResponse {
  page: number;
  pageSize: number;
  total: number;
  items: AdminListItem[];
}

export interface AdminDetail {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
  identityId: string | null;
}

/** Result of the create form's "Check IdM" lookup. */
export interface CheckIdmResult {
  found: boolean;
  id: string | null;
  email: string | null;
  name: string | null;
  roles: string[];
  alreadyAdmin: boolean;
}

export interface CreateAdminRequest {
  email: string;
  name: string;
  idmExistingUserId?: string | null;
}

export interface UpdateAdminRequest {
  name: string;
  isActive: boolean;
}

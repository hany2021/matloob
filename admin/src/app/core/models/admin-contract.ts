/** Admin contract-list models — mirror the .NET admin Contracts DTOs (camelCase JSON). */

export interface AdminContractListItem {
  id: string;
  applicantName: string | null;
  applicantType: string | null;
  providerName: string | null;
  opportunityName: string | null;
  status: string;
  statusLabel: string;
  statusColor: string;
  startDate: string | null;
  endDate: string | null;
  acceptedAt: string | null;
  monthlySalary: number | null;
  createdAt: string;
}

export interface ListAdminContractsResponse {
  page: number;
  pageSize: number;
  total: number;
  items: AdminContractListItem[];
}

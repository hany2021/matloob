/**
 * Wire shapes for the admin establishment endpoints
 * (`/api/v1/admin/establishments/...`). These endpoints serialize with
 * the FastEndpoints default web policy (camelCase), unlike the public
 * composite reads that use explicit snake_case via JsonPropertyName.
 */

export type EstablishmentStatusCamel =
  | 'Draft'
  | 'PendingReview'
  | 'Approved'
  | 'Rejected'
  | 'Suspended';

export interface PendingReviewItem {
  id: string;
  name: string;
  commercialRegistrationNumber: string;
  city: string;
  submittedAt: string;
  createdByUserId: string;
}

export interface PendingReviewResponse {
  page: number;
  pageSize: number;
  total: number;
  items: PendingReviewItem[];
}

export interface ReviewDocument {
  id: string;
  documentType: string;
  assetId: string;
  uploadedByUserId: string;
  uploadedAt: string;
}

export interface ReviewDetail {
  id: string;
  status: EstablishmentStatusCamel;
  createdByUserId: string;
  createdAt: string;
  submittedAt: string | null;
  approvedAt: string | null;
  approvedByAdminId: string | null;
  rejectedAt: string | null;
  rejectedByAdminId: string | null;
  rejectionReason: string | null;
  suspendedAt: string | null;
  suspendedByAdminId: string | null;
  suspensionReason: string | null;
  isSponsor: boolean;
  canManageEvents: boolean;
  isLegacyImport: boolean;
  name: string;
  commercialRegistrationNumber: string;
  commercialRegistrationExpiry: string | null;
  laborOfficeId: string;
  sequenceNumber: string;
  city: string;
  email: string;
  phone: string;
  economicActivity: string | null;
  subEconomicActivity: string | null;
  district: string | null;
  area: string | null;
  street: string | null;
  description: string | null;
  locationTitle: string | null;
  latitude: number | null;
  longitude: number | null;
  buildingNumber: string | null;
  postalCode: string | null;
  additionalNumber: string | null;
  website: string | null;
  yearsOfExperience: number | null;
  establishmentSize: string | null;
  additionalContactNumber: string | null;
  documents: ReviewDocument[];
}

export interface ReviewHistoryItem {
  id: string;
  action: string;
  occurredAt: string;
  actorUserId: string | null;
  actorAdminId: string | null;
  reason: string | null;
  changeRequestId: string | null;
  snapshotJson: string | null;
}

export interface ReviewHistoryResponse {
  establishmentId: string;
  items: ReviewHistoryItem[];
}

export interface PendingChangeRequestItem {
  id: string;
  establishmentId: string;
  establishmentName: string;
  commercialRegistrationNumber: string;
  submittedAt: string;
  createdByUserId: string;
}

export interface PendingChangeRequestsResponse {
  page: number;
  pageSize: number;
  total: number;
  items: PendingChangeRequestItem[];
}

export interface MemberSummary {
  id: string;
  userId: string;
  role: 'Owner' | 'Manager' | 'Other';
  isActive: boolean;
  addedAt: string;
  addedByUserId: string;
}

export interface ListMembersResponse {
  establishmentId: string;
  members: MemberSummary[];
}

export interface AddMemberRequest {
  userId: string;
  role: 'Owner' | 'Manager' | 'Other';
}

export interface UpdateMemberRequest {
  role?: 'Owner' | 'Manager' | 'Other' | null;
  isActive?: boolean | null;
}

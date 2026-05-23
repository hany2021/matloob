/**
 * Wire shapes for the canonical `/api/v1/establishments/*` reads. These
 * endpoints use the FastEndpoints default web policy → camelCase (the
 * legacy Laravel-shaped `/api/establishments/me/profile` endpoint is
 * snake_case and lives outside the admin's surface).
 */

export type EstablishmentStatus =
  | 'Draft'
  | 'PendingReview'
  | 'Approved'
  | 'Rejected'
  | 'Suspended';

export type EstablishmentMemberRole = 'Owner' | 'Manager' | 'Other';

/** Item returned by `GET /api/v1/establishments` (self-list). */
export interface EstablishmentSummary {
  id: string;
  name: string;
  commercialRegistrationNumber: string;
  status: EstablishmentStatus;
  city: string;
  createdByUserId: string;
  submittedAt: string | null;
  approvedAt: string | null;
  rejectedAt: string | null;
  suspendedAt: string | null;
  isLegacyImport: boolean;
  myRole: EstablishmentMemberRole | null;
  canEdit: boolean;
  canSubmit: boolean;
  canManageMembers: boolean;
  canCreateChangeRequest: boolean;
}

export interface ListMineResponse {
  items: EstablishmentSummary[];
}

export interface EstablishmentDocumentSummary {
  documentType: string;
  assetId: string;
  uploadedAt: string;
}

export interface EstablishmentMemberDetailsSummary {
  id: string;
  userId: string;
  role: EstablishmentMemberRole;
  addedAt: string;
}

export interface PendingChangeRequestSummary {
  id: string;
  status: 'Draft' | 'PendingReview' | string;
  createdAt: string;
  submittedAt: string | null;
  createdByUserId: string;
}

/** Response from `GET /api/v1/establishments/{id}`. */
export interface EstablishmentDetail {
  id: string;
  status: EstablishmentStatus;
  createdByUserId: string;
  createdAt: string;
  submittedAt: string | null;
  approvedAt: string | null;
  rejectedAt: string | null;
  rejectionReason: string | null;
  suspendedAt: string | null;
  suspensionReason: string | null;
  isSponsor: boolean;
  canManageEvents: boolean;
  isLegacyImport: boolean;
  myRole: EstablishmentMemberRole | null;
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
  documents: EstablishmentDocumentSummary[];
  members: EstablishmentMemberDetailsSummary[] | null;
  pendingChangeRequest: PendingChangeRequestSummary | null;
}

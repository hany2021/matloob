/**
 * Establishment statuses that drive the status-badge UI. Mirrors the
 * .NET <c>EstablishmentStatus</c> enum string values.
 */
export type EstablishmentStatus =
  | 'Draft'
  | 'PendingReview'
  | 'Approved'
  | 'Rejected'
  | 'Suspended';

export interface EstablishmentSummary {
  id: string;
  name: string;
  email: string;
  status: EstablishmentStatus;
  createdAt?: string;
  submittedAt?: string | null;
  approvedAt?: string | null;
  commercialRegistrationNumber?: string;
  laborOfficeId?: string;
  sequenceNumber?: string;
}

export interface EstablishmentDocumentSummary {
  documentType: string;
  assetId: string;
  uploadedAt: string;
}

export interface EstablishmentMemberSummary {
  id: string;
  userId: string;
  role: 'Owner' | 'Manager' | 'Other';
  addedAt: string;
}

export interface PendingChangeRequestSummary {
  id: string;
  status: 'Draft' | 'PendingReview';
  createdAt: string;
  submittedAt: string | null;
  createdByUserId: string;
}

export interface EstablishmentDetail extends EstablishmentSummary {
  status: EstablishmentStatus;
  createdByUserId: string;
  rejectionReason?: string | null;
  suspendedAt?: string | null;
  suspensionReason?: string | null;
  isSponsor: boolean;
  canManageEvents: boolean;
  isLegacyImport?: boolean;
  myRole?: 'Owner' | 'Manager' | 'Other' | null;
  commercialRegistrationExpiry?: string | null;
  city?: string;
  phone?: string;
  economicActivity?: string | null;
  subEconomicActivity?: string | null;
  district?: string | null;
  area?: string | null;
  street?: string | null;
  description?: string | null;
  locationTitle?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  buildingNumber?: string | null;
  postalCode?: string | null;
  additionalNumber?: string | null;
  website?: string | null;
  yearsOfExperience?: number | null;
  establishmentSize?: string | null;
  additionalContactNumber?: string | null;
  documents: EstablishmentDocumentSummary[];
  members?: EstablishmentMemberSummary[] | null;
  pendingChangeRequest?: PendingChangeRequestSummary | null;
}

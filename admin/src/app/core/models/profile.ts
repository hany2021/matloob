/**
 * Current user's profile, mirroring `GET /api/v1/profile`. Many fields
 * are placeholders the backend emits as null/[] until their slice
 * migrates; the admin UI surfaces only what's populated today.
 */
export interface UserProfile {
  id: string;
  name: string | null;
  email: string | null;
  phone_number: string | null;
  identity_id: string;
  onboarded: boolean;
  profile_complete_percentage: number;
  uncompleted_profile_sections: string[];
}

/**
 * Item returned by `GET /api/v1/users/profile/establishment-list`. Each
 * entry is one establishment the caller is an active member of, with
 * its status and the caller's role added as new-client extensions.
 */
export interface MyEstablishmentListItem {
  id: string;
  name: string;
  type: string;
  logo: string | null;
  labor_office_id: string;
  sequence_number: string;
  status: string;
  role: 'Owner' | 'Manager' | 'Other' | string;
}

/**
 * Snapshot used by the shell when the app boots: profile plus the list
 * of establishments the caller can act on. Not produced by a single
 * backend endpoint — the client assembles it from the two profile
 * reads.
 */
export interface InitData {
  profile: UserProfile;
  establishments: MyEstablishmentListItem[];
}

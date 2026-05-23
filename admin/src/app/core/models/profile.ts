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
 * Membership entry returned by the establishment list endpoint
 * (`GET /api/v1/profile/establishments`). The admin shell uses this to
 * disambiguate when a user belongs to multiple establishments.
 */
export interface MyEstablishmentMembership {
  id: string;
  name: string;
  status: string;
  role: 'Owner' | 'Manager' | 'Other';
  is_sponsor: boolean;
  can_manage_events: boolean;
}

export interface InitData {
  identity_id: string;
  user: UserProfile | null;
  establishments: MyEstablishmentMembership[];
}

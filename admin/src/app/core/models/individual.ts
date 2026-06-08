/** Individual user account models — mirror the .NET admin Individuals DTOs (camelCase JSON). */

export interface IndividualListItem {
  id: string;
  idNumber: string | null;
  name: string;
  email: string;
  phone: string | null;
  gender: string | null;
  age: number | null;
  city: string | null;
  nationality: string | null;
  yearsOfExperience: number;
}

export interface ListIndividualsResponse {
  page: number;
  pageSize: number;
  total: number;
  items: IndividualListItem[];
}

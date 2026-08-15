import type { components } from './api-schema';

/**
 * Apelidos para os tipos gerados do OpenAPI. Nenhum DTO e escrito a mao: remover um
 * campo no C# quebra o build do TypeScript, que e o momento certo de descobrir.
 * Regenerar com `npm run generate:api`. Ver docs/adr/0009-cliente-gerado-do-openapi.md.
 */
type Schemas = components['schemas'];

export type AuthResponse = Schemas['AuthResponse'];
export type LoginRequest = Schemas['LoginRequest'];
export type RegisterCompanyRequest = Schemas['RegisterCompanyRequest'];
export type MeResponse = Schemas['MeResponse'];

export type EmployeeResponse = Schemas['EmployeeResponse'];
export type EmployeeListItem = Schemas['EmployeeListItem'];
export type CreateEmployeeRequest = Schemas['CreateEmployeeRequest'];
export type UpdateEmployeeRequest = Schemas['UpdateEmployeeRequest'];
export type TerminateEmployeeRequest = Schemas['TerminateEmployeeRequest'];
export type PagedEmployees = Schemas['PagedResultOfEmployeeListItem'];

export type DepartmentNode = Schemas['DepartmentNode'];
export type CreateDepartmentRequest = Schemas['CreateDepartmentRequest'];
export type UpdateDepartmentRequest = Schemas['UpdateDepartmentRequest'];
export type PositionResponse = Schemas['PositionResponse'];
export type CreatePositionRequest = Schemas['CreatePositionRequest'];

export type ContractResponse = Schemas['ContractResponse'];
export type SaveContractRequest = Schemas['SaveContractRequest'];
export type EmploymentRegime = Schemas['EmploymentRegime'];
export type WorkScheduleType = Schemas['WorkScheduleType'];

export type InviteResponse = Schemas['InviteResponse'];
export type InviteStatus = Schemas['InviteStatus'];
export type InvitePreview = Schemas['InvitePreview'];
export type CreateInviteRequest = Schemas['CreateInviteRequest'];

export type EmployeeImportFormat = Schemas['EmployeeImportFormat'];
export type EmployeeImportPreview = Schemas['EmployeeImportPreview'];
export type EmployeeImportColumn = Schemas['EmployeeImportColumn'];
export type EmployeeImportRow = Schemas['EmployeeImportRow'];
export type EmployeeImportRowStatus = Schemas['EmployeeImportRowStatus'];
export type EmployeeImportSummary = Schemas['EmployeeImportSummary'];
export type EmployeeImportResult = Schemas['EmployeeImportResult'];

/**
 * Forma unica de erro vinda do backend. O `code` e estavel e serve para o frontend
 * decidir; `fieldErrors` alimenta o formulario direto, sem codigo por tela.
 */
export interface ApiProblem {
  title: string;
  detail: string;
  status: number;
  code?: string;
  traceId?: string;
  fieldErrors?: Record<string, string[]>;
}

export type AvailabilityResponse = Schemas['AvailabilityResponse'];
export type OccupancyResponse = Schemas['OccupancyResponse'];
export type CreateOccupancyRequest = Schemas['CreateOccupancyRequest'];
export type OccupancyKind = Schemas['OccupancyKind'];

export type AbsenceRequestResponse = Schemas['AbsenceRequestResponse'];
export type CreateAbsenceRequest = Schemas['CreateAbsenceRequest'];
export type AbsenceRequestStatus = Schemas['AbsenceRequestStatus'];

export type MissionResponse = Schemas['MissionResponse'];
export type CreateMissionRequest = Schemas['CreateMissionRequest'];
export type MissionSuggestion = Schemas['MissionSuggestion'];
export type SuggestedPerson = Schemas['SuggestedPerson'];

export type WorkBoard = Schemas['WorkBoard'];
export type WorkItemResponse = Schemas['WorkItemResponse'];
export type WorkItemStatus = Schemas['WorkItemStatus'];
export type WorkItemPriority = Schemas['WorkItemPriority'];
export type WorkAlert = Schemas['WorkAlert'];
export type WorkAlertKind = Schemas['WorkAlertKind'];
export type CreateWorkItemRequest = Schemas['CreateWorkItemRequest'];
export type UpdateWorkItemRequest = Schemas['UpdateWorkItemRequest'];

export type UpdatePositionRequest = Schemas['UpdatePositionRequest'];

export type RotationPolicyRequest = Schemas['RotationPolicyRequest'];
export type RotationPolicyResponse = Schemas['RotationPolicyResponse'];
export type RotationTiebreak = Schemas['RotationTiebreak'];
export type RestrictionResponse = Schemas['RestrictionResponse'];
export type CreateRestrictionRequest = Schemas['CreateRestrictionRequest'];

export type TodayPanel = Schemas['TodayPanel'];
export type TodayPerson = Schemas['TodayPerson'];
export type TodayMission = Schemas['TodayMission'];
export type TodayWorkItem = Schemas['TodayWorkItem'];
export type TodayApproval = Schemas['TodayApproval'];

export type SearchResponse = Schemas['SearchResponse'];
export type SearchResult = Schemas['SearchResult'];
export type SearchResultKind = Schemas['SearchResultKind'];

export type TodayScope = Schemas['TodayScope'];
export type TodaySection = Schemas['TodaySection'];

export type AuditPage = Schemas['AuditPage'];
export type AuditEntryResponse = Schemas['AuditEntryResponse'];

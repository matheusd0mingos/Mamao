import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateDepartmentRequest,
  CreatePositionRequest,
  DepartmentNode,
  PositionResponse,
  UpdatePositionRequest,
  UpdateDepartmentRequest,
} from '../../core/http/api.types';

/** Setores e cargos. Ver docs/produto/modelo-de-dominio.md#people. */
@Injectable({ providedIn: 'root' })
export class OrganizationApi {
  private readonly http = inject(HttpClient);

  listDepartments(): Promise<DepartmentNode[]> {
    return firstValueFrom(this.http.get<DepartmentNode[]>('/api/v1/departments'));
  }

  createDepartment(request: CreateDepartmentRequest): Promise<DepartmentNode> {
    return firstValueFrom(this.http.post<DepartmentNode>('/api/v1/departments', request));
  }

  updateDepartment(id: string, request: UpdateDepartmentRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/v1/departments/${id}`, request));
  }

  deleteDepartment(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/departments/${id}`));
  }

  listPositions(): Promise<PositionResponse[]> {
    return firstValueFrom(this.http.get<PositionResponse[]>('/api/v1/positions'));
  }

  createPosition(request: CreatePositionRequest): Promise<PositionResponse> {
    return firstValueFrom(this.http.post<PositionResponse>('/api/v1/positions', request));
  }

  updatePosition(id: string, request: UpdatePositionRequest): Promise<PositionResponse> {
    return firstValueFrom(this.http.put<PositionResponse>(`/api/v1/positions/${id}`, request));
  }

  deletePosition(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/positions/${id}`));
  }
}

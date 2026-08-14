import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateWorkItemRequest,
  UpdateWorkItemRequest,
  WorkBoard,
  WorkItemResponse,
  WorkItemStatus,
} from '../../core/http/api.types';

/** Demandas. O quadro ja vem com o alerta de cada cartao calculado no servidor. */
@Injectable({ providedIn: 'root' })
export class WorkApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/work';

  board(filtros: {
    departmentId?: string | null;
    assigneeId?: string | null;
    includeDone?: boolean;
  }): Promise<WorkBoard> {
    let params = new HttpParams();
    if (filtros.departmentId) params = params.set('departmentId', filtros.departmentId);
    if (filtros.assigneeId) params = params.set('assigneeId', filtros.assigneeId);
    if (filtros.includeDone) params = params.set('includeDone', 'true');

    return firstValueFrom(this.http.get<WorkBoard>(this.base, { params }));
  }

  create(request: CreateWorkItemRequest): Promise<WorkItemResponse> {
    return firstValueFrom(this.http.post<WorkItemResponse>(this.base, request));
  }

  update(id: string, request: UpdateWorkItemRequest): Promise<WorkItemResponse> {
    return firstValueFrom(this.http.put<WorkItemResponse>(`${this.base}/${id}`, request));
  }

  /** Arrastar de coluna tem rota propria: nao reenvia o resto do cartao. */
  move(id: string, status: WorkItemStatus): Promise<WorkItemResponse> {
    return firstValueFrom(this.http.post<WorkItemResponse>(`${this.base}/${id}/move`, { status }));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}

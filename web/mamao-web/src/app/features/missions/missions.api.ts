import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateMissionRequest,
  MissionResponse,
  MissionSuggestion,
} from '../../core/http/api.types';

/** Missoes e montagem de escala. Ver docs/adr/0019-escala-por-rodizio.md. */
@Injectable({ providedIn: 'root' })
export class MissionsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/missions';

  list(): Promise<MissionResponse[]> {
    return firstValueFrom(this.http.get<MissionResponse[]>(this.base));
  }

  create(request: CreateMissionRequest): Promise<MissionResponse> {
    return firstValueFrom(this.http.post<MissionResponse>(this.base, request));
  }

  /** Quem pode ir, na ordem do rodizio, com o motivo de cada posicao. */
  suggestion(id: string): Promise<MissionSuggestion> {
    return firstValueFrom(this.http.get<MissionSuggestion>(`${this.base}/${id}/suggestion`));
  }

  assign(id: string, employeeIds: string[]): Promise<MissionResponse> {
    return firstValueFrom(
      this.http.put<MissionResponse>(`${this.base}/${id}/assignments`, { employeeIds }),
    );
  }

  confirm(id: string): Promise<MissionResponse> {
    return firstValueFrom(this.http.post<MissionResponse>(`${this.base}/${id}/confirm`, {}));
  }

  cancel(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/${id}/cancel`, {}));
  }
}

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { CreateRestrictionRequest, RestrictionResponse } from '../../core/http/api.types';

/** Quem nao entra em certa atividade. Ver docs/adr/0019-escala-por-rodizio.md. */
@Injectable({ providedIn: 'root' })
export class RestrictionsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/restrictions';

  list(employeeId?: string): Promise<RestrictionResponse[]> {
    const params = employeeId ? new HttpParams().set('employeeId', employeeId) : undefined;
    return firstValueFrom(this.http.get<RestrictionResponse[]>(this.base, { params }));
  }

  create(request: CreateRestrictionRequest): Promise<RestrictionResponse> {
    return firstValueFrom(this.http.post<RestrictionResponse>(this.base, request));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}

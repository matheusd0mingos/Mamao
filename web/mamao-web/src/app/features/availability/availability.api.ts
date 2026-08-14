import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  AbsenceRequestResponse,
  AbsenceRequestStatus,
  AvailabilityResponse,
  CreateAbsenceRequest,
  CreateOccupancyRequest,
  OccupancyResponse,
} from '../../core/http/api.types';

/** Disponibilidade e solicitacoes. Ver docs/adr/0021-solicitacao-e-ocupacao.md. */
@Injectable({ providedIn: 'root' })
export class AvailabilityApi {
  private readonly http = inject(HttpClient);

  /** Quem pode num dia, opcionalmente num recorte de horario. */
  on(dia: string, de?: string, ate?: string): Promise<AvailabilityResponse[]> {
    let params = new HttpParams().set('on', dia);
    if (de && ate) params = params.set('from', de).set('to', ate);

    return firstValueFrom(
      this.http.get<AvailabilityResponse[]>('/api/v1/availability', { params }),
    );
  }

  occupancies(de: string, ate: string, employeeId?: string): Promise<OccupancyResponse[]> {
    let params = new HttpParams().set('from', de).set('to', ate);
    if (employeeId) params = params.set('employeeId', employeeId);

    return firstValueFrom(
      this.http.get<OccupancyResponse[]>('/api/v1/availability/occupancies', { params }),
    );
  }

  createOccupancy(request: CreateOccupancyRequest): Promise<OccupancyResponse> {
    return firstValueFrom(
      this.http.post<OccupancyResponse>('/api/v1/availability/occupancies', request),
    );
  }

  deleteOccupancy(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/availability/occupancies/${id}`));
  }

  requests(status?: AbsenceRequestStatus): Promise<AbsenceRequestResponse[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return firstValueFrom(
      this.http.get<AbsenceRequestResponse[]>('/api/v1/absence-requests', { params }),
    );
  }

  createRequest(request: CreateAbsenceRequest): Promise<AbsenceRequestResponse> {
    return firstValueFrom(
      this.http.post<AbsenceRequestResponse>('/api/v1/absence-requests', request),
    );
  }

  approve(id: string, note?: string): Promise<AbsenceRequestResponse> {
    return firstValueFrom(
      this.http.post<AbsenceRequestResponse>(`/api/v1/absence-requests/${id}/approve`, {
        note: note ?? null,
      }),
    );
  }

  reject(id: string, note?: string): Promise<AbsenceRequestResponse> {
    return firstValueFrom(
      this.http.post<AbsenceRequestResponse>(`/api/v1/absence-requests/${id}/reject`, {
        note: note ?? null,
      }),
    );
  }
}

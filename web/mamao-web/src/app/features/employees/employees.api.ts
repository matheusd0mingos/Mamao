import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateEmployeeRequest,
  EmployeeResponse,
  PagedEmployees,
  TerminateEmployeeRequest,
  UpdateEmployeeRequest,
} from '../../core/http/api.types';

/**
 * Camada fina sobre o HttpClient com os tipos GERADOS do OpenAPI.
 *
 * Nao usamos um cliente fetch gerado de proposito: ele passaria por fora dos
 * interceptors do Angular, e e neles que moram auth, refresh de token e traducao de erro.
 * Tipos gerados + HttpClient da os dois lados. Ver docs/adr/0009-cliente-gerado-do-openapi.md.
 */
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/employees';

  list(search: string, includeInactive: boolean, page: number, pageSize: number): Promise<PagedEmployees> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize)
      .set('includeInactive', includeInactive);

    if (search.trim()) {
      params = params.set('search', search.trim());
    }

    return firstValueFrom(this.http.get<PagedEmployees>(this.base, { params }));
  }

  get(id: string): Promise<EmployeeResponse> {
    return firstValueFrom(this.http.get<EmployeeResponse>(`${this.base}/${id}`));
  }

  create(request: CreateEmployeeRequest): Promise<EmployeeResponse> {
    return firstValueFrom(this.http.post<EmployeeResponse>(this.base, request));
  }

  update(id: string, request: UpdateEmployeeRequest): Promise<EmployeeResponse> {
    return firstValueFrom(this.http.put<EmployeeResponse>(`${this.base}/${id}`, request));
  }

  terminate(id: string, request: TerminateEmployeeRequest): Promise<EmployeeResponse> {
    return firstValueFrom(this.http.post<EmployeeResponse>(`${this.base}/${id}/terminate`, request));
  }
}

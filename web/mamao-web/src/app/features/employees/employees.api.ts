import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateEmployeeRequest,
  EmployeeImportFormat,
  EmployeeImportPreview,
  EmployeeImportResult,
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

  /** O formato esperado vem do servidor: os nomes de coluna aceitos tem uma verdade so. */
  importFormat(): Promise<EmployeeImportFormat> {
    return firstValueFrom(this.http.get<EmployeeImportFormat>(`${this.base}/import/format`));
  }

  /**
   * O modelo passa pelo HttpClient (e nao por um <a href>) porque o endpoint exige o
   * bearer, que so o interceptor sabe anexar.
   */
  downloadTemplate(): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`${this.base}/import/template`, { responseType: 'blob' }),
    );
  }

  previewImport(arquivo: File): Promise<EmployeeImportPreview> {
    return firstValueFrom(
      this.http.post<EmployeeImportPreview>(`${this.base}/import/preview`, corpo(arquivo)),
    );
  }

  import(arquivo: File): Promise<EmployeeImportResult> {
    return firstValueFrom(
      this.http.post<EmployeeImportResult>(`${this.base}/import`, corpo(arquivo)),
    );
  }
}

/**
 * FormData de proposito: o arquivo sobe como multipart, sem base64. O nome do campo tem
 * que ser "arquivo" — e o nome do parametro IFormFile no endpoint.
 * Nao definimos Content-Type: o navegador precisa gerar o boundary.
 */
function corpo(arquivo: File): FormData {
  const dados = new FormData();
  dados.append('arquivo', arquivo, arquivo.name);
  return dados;
}

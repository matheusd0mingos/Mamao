import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { DocumentResponse, UpdateDocumentRequest } from '../../core/http/api.types';

/** Documentos com validade. O upload e multipart — arquivo em JSON cresce um terco. */
@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/documents';

  list(employeeId?: string, expiring = false): Promise<DocumentResponse[]> {
    let params = new HttpParams();
    if (employeeId) params = params.set('employeeId', employeeId);
    if (expiring) params = params.set('expiring', 'true');

    return firstValueFrom(this.http.get<DocumentResponse[]>(this.base, { params }));
  }

  upload(dados: {
    employeeId: string;
    kind: string;
    issuedOn?: string | null;
    expiresOn?: string | null;
    notes?: string | null;
    file: File;
  }): Promise<DocumentResponse> {
    const form = new FormData();
    form.append('employeeId', dados.employeeId);
    form.append('kind', dados.kind);
    if (dados.issuedOn) form.append('issuedOn', dados.issuedOn);
    if (dados.expiresOn) form.append('expiresOn', dados.expiresOn);
    if (dados.notes) form.append('notes', dados.notes);
    form.append('file', dados.file, dados.file.name);

    return firstValueFrom(this.http.post<DocumentResponse>(this.base, form));
  }

  update(id: string, request: UpdateDocumentRequest): Promise<DocumentResponse> {
    return firstValueFrom(this.http.put<DocumentResponse>(`${this.base}/${id}`, request));
  }

  /**
   * Baixa como blob e dispara o salvamento.
   *
   * Nao e um <a href> simples porque a rota exige o cabecalho Authorization — um link
   * comum iria sem token e voltaria 401.
   */
  async download(id: string, fileName: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`${this.base}/${id}/file`, { responseType: 'blob' }),
    );

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}

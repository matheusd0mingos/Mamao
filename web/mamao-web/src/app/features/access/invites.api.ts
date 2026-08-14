import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  AuthResponse,
  CreateInviteRequest,
  InvitePreview,
  InviteResponse,
} from '../../core/http/api.types';

/** Convites de acesso. Ver docs/roadmap.md, Marco 1. */
@Injectable({ providedIn: 'root' })
export class InvitesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/invites';

  list(): Promise<InviteResponse[]> {
    return firstValueFrom(this.http.get<InviteResponse[]>(this.base));
  }

  create(request: CreateInviteRequest): Promise<InviteResponse> {
    return firstValueFrom(this.http.post<InviteResponse>(this.base, request));
  }

  resend(id: string): Promise<InviteResponse> {
    return firstValueFrom(this.http.post<InviteResponse>(`${this.base}/${id}/resend`, {}));
  }

  revoke(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }

  /** Anônimos: quem está aceitando ainda não tem conta. */
  preview(token: string): Promise<InvitePreview> {
    return firstValueFrom(
      this.http.get<InvitePreview>(`${this.base}/preview`, { params: { token } }),
    );
  }

  accept(token: string, password: string): Promise<AuthResponse> {
    return firstValueFrom(this.http.post<AuthResponse>(`${this.base}/accept`, { token, password }));
  }
}

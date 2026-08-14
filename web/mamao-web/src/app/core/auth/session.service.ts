import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { AuthResponse, LoginResponse, TenantOption } from '../http/api.types';

const STORAGE_KEY = 'mamao.session';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  tenantId: string;
  tenantName: string;
  role: string;
  permissions: string[];
}

/**
 * Sessao do usuario. Estado em signals, sem NgRx — a complexidade real do Mamao nao
 * justifica actions/reducers/effects. Ver docs/adr/0008-frontend-angular.md.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(restore());

  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly tenantName = computed(() => this.session()?.tenantName ?? '');
  readonly role = computed(() => this.session()?.role ?? '');
  readonly permissions = computed(() => this.session()?.permissions ?? []);

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  get refreshToken(): string | null {
    return this.session()?.refreshToken ?? null;
  }

  /**
   * O frontend esconde para NAO FRUSTRAR; o backend impede para PROTEGER. Toda checagem
   * aqui tem policy correspondente no endpoint. Ver docs/adr/0007-autorizacao.md.
   */
  has(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  async login(email: string, password: string, tenantId?: string): Promise<TenantOption[] | null> {
    const response = await firstValueFrom(
      this.http.post<LoginResponse>('/api/v1/auth/login', { email, password, tenantId: tenantId ?? null }),
    );

    // Mesma pessoa em varias empresas: o servidor devolve a lista e o login se repete
    // com o tenant escolhido.
    if (response.requiresTenantSelection) {
      return response.tenants ?? [];
    }

    this.store(response.auth!);
    return null;
  }

  async registerCompany(companyName: string, fullName: string, email: string, password: string): Promise<void> {
    const auth = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/register-company', { companyName, fullName, email, password }),
    );

    this.store(auth);
  }

  async refresh(): Promise<boolean> {
    const token = this.refreshToken;
    if (!token) return false;

    try {
      const auth = await firstValueFrom(
        this.http.post<AuthResponse>('/api/v1/auth/refresh', { refreshToken: token }),
      );
      this.store(auth);
      return true;
    } catch {
      this.logout();
      return false;
    }
  }

  logout(): void {
    this.session.set(null);
    localStorage.removeItem(STORAGE_KEY);
    void this.router.navigate(['/entrar']);
  }

  private store(auth: AuthResponse): void {
    const stored: StoredSession = {
      accessToken: auth.accessToken,
      refreshToken: auth.refreshToken,
      expiresAt: auth.expiresAt,
      tenantId: auth.tenantId,
      tenantName: auth.tenantName,
      role: auth.role,
      permissions: auth.permissions ?? [],
    };

    this.session.set(stored);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
  }
}

function restore(): StoredSession | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as StoredSession;
  } catch {
    localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

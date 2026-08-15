import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { SearchResponse, TodayPanel } from '../../core/http/api.types';

/** O painel de hoje e a busca global: as duas rotas que abrem o sistema. */
@Injectable({ providedIn: 'root' })
export class TodayApi {
  private readonly http = inject(HttpClient);

  today(): Promise<TodayPanel> {
    return firstValueFrom(this.http.get<TodayPanel>('/api/v1/today'));
  }

  search(q: string): Promise<SearchResponse> {
    return firstValueFrom(
      this.http.get<SearchResponse>('/api/v1/search', { params: new HttpParams().set('q', q) }),
    );
  }
}

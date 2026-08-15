import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { SearchResponse, TodayPanel } from '../../core/http/api.types';

/** O painel de hoje e a busca global: as duas rotas que abrem o sistema. */
@Injectable({ providedIn: 'root' })
export class TodayApi {
  private readonly http = inject(HttpClient);

  /**
   * `departmentId` nulo pede o escopo natural de quem esta olhando; `all` pede a empresa
   * inteira DE PROPOSITO. Sao coisas diferentes: sem o `all`, o chefe de secao que
   * escolhesse "a empresa inteira" caia de volta na propria secao.
   */
  today(departmentId?: string | null, all = false): Promise<TodayPanel> {
    let params = new HttpParams();
    if (departmentId) params = params.set('departmentId', departmentId);
    if (all) params = params.set('all', 'true');

    return firstValueFrom(this.http.get<TodayPanel>('/api/v1/today', { params }));
  }

  search(q: string): Promise<SearchResponse> {
    return firstValueFrom(
      this.http.get<SearchResponse>('/api/v1/search', { params: new HttpParams().set('q', q) }),
    );
  }
}

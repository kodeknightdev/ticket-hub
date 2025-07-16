import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Alert, BreakdownRow, CancellationSaga, FilterOptions, Funnel, Heatmap, Insights, LeadTime, Overview, PerformanceRow, PricingImpact,
  RevenueForecast, SectionHeatmap, TimePoint,
} from './models';

export type BreakdownBy = 'event' | 'artist' | 'category' | 'city' | 'venue';

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);

  overview(params: HttpParams): Observable<Overview> {
    return this.http.get<Overview>('/bff/overview', { params });
  }

  timeseries(params: HttpParams): Observable<{ granularity: string; points: TimePoint[] }> {
    return this.http.get<{ granularity: string; points: TimePoint[] }>('/bff/sales/timeseries', { params });
  }

  breakdown(params: HttpParams, by: BreakdownBy): Observable<{ by: string; rows: BreakdownRow[] }> {
    return this.http.get<{ by: string; rows: BreakdownRow[] }>('/bff/breakdowns', { params: params.set('by', by) });
  }

  sectionHeatmap(params: HttpParams): Observable<SectionHeatmap> {
    return this.http.get<SectionHeatmap>('/bff/sections/heatmap', { params });
  }

  performance(params: HttpParams): Observable<{ rows: PerformanceRow[]; partial: boolean }> {
    return this.http.get<{ rows: PerformanceRow[]; partial: boolean }>('/bff/events/performance', { params });
  }

  insights(eventId: string): Observable<Insights> {
    return this.http.get<Insights>(`/bff/events/${eventId}/insights`);
  }

  revenueForecast(params: HttpParams, horizon: 30 | 60 | 90): Observable<RevenueForecast> {
    return this.http.get<RevenueForecast>('/bff/forecast/revenue', { params: params.set('horizon', horizon) });
  }

  demandHeatmap(params: HttpParams): Observable<Heatmap> {
    return this.http.get<Heatmap>('/bff/demand/heatmap', { params });
  }

  leadTime(params: HttpParams): Observable<LeadTime> {
    return this.http.get<LeadTime>('/bff/demand/lead-time', { params });
  }

  funnel(params: HttpParams): Observable<Funnel> {
    return this.http.get<Funnel>('/bff/funnel', { params });
  }

  pricingImpact(params: HttpParams): Observable<PricingImpact> {
    return this.http.get<PricingImpact>('/bff/pricing-impact', { params });
  }

  alerts(params: HttpParams): Observable<Alert[]> {
    return this.http.get<Alert[]>('/bff/alerts', { params });
  }

  cancellations(): Observable<CancellationSaga[]> {
    return this.http.get<CancellationSaga[]>('/bff/cancellations');
  }

  filters(params: HttpParams): Observable<FilterOptions> {
    return this.http.get<FilterOptions>('/bff/filters', { params });
  }

  exportUrl(report: string, params: HttpParams): string {
    return `/bff/export/${report}.csv?${params.toString()}`;
  }
}

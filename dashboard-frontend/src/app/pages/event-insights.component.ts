import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import { DashboardApiService } from '../core/dashboard-api.service';
import { Insights } from '../core/models';
import { salesCurve, velocityWithPrices } from '../shared/charts';
import { PaceChipComponent } from '../shared/pace-chip.component';

@Component({
  selector: 'app-event-insights',
  imports: [MatCardModule, MatProgressBarModule, NgxEchartsDirective, RouterLink, DatePipe, PercentPipe, DecimalPipe, PaceChipComponent],
  template: `
    <a routerLink="/events">← Event performance</a>
    @if (data(); as d) {
      <h2>{{ d.insights.event.title }}</h2>
      <p class="sub">{{ d.insights.event.venue }}, {{ d.insights.event.city }} · {{ d.insights.event.startsAt | date: 'EEE d MMM y, HH:mm' }}</p>
      @if (d.insights.summary; as s) {
        <section class="summary">
          <mat-card><div class="label">Sold so far</div><div class="value">{{ s.soldToDate | number }} / {{ d.insights.event.capacity | number }}</div></mat-card>
          <mat-card><div class="label">Projected final sell-through</div><div class="value">{{ s.projectedSellThrough | percent }}</div></mat-card>
          <mat-card>
            <div class="label">Predicted sell-out</div>
            <div class="value">{{ s.predictedSelloutDate ? (s.predictedSelloutDate | date: 'd MMM') : 'not expected' }}</div>
          </mat-card>
          <mat-card><div class="label">Pace</div><div class="value"><app-pace-chip [status]="s.paceStatus" /></div><div class="label">model: {{ s.model }}</div></mat-card>
        </section>
      }
      <mat-card>
        <mat-card-header><mat-card-title>Cumulative sales vs forecast (80% band)</mat-card-title></mat-card-header>
        <div class="chart" echarts [options]="curve()"></div>
      </mat-card>
      <mat-card>
        <mat-card-header><mat-card-title>Sales velocity and price changes</mat-card-title></mat-card-header>
        <div class="chart" echarts [options]="velocity()"></div>
      </mat-card>
      <mat-card>
        <mat-card-header><mat-card-title>Section fill</mat-card-title></mat-card-header>
        <mat-card-content>
          @for (section of d.insights.sections; track section.sectionId) {
            <div class="fill">
              <span>{{ section.name }}</span>
              <mat-progress-bar mode="determinate" [value]="section.fill * 100" />
              <span>{{ section.sold }} / {{ section.capacity }}</span>
            </div>
          }
        </mat-card-content>
      </mat-card>
    } @else if (missing()) {
      <p>This event isn't in your analytics scope.</p>
    }
  `,
  styles: `
    .sub { color: #666; margin-top: -8px; }
    .summary { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 16px; margin-bottom: 16px; }
    .summary mat-card { padding: 16px; }
    .label { font-size: 12px; color: #666; }
    .value { font-size: 22px; font-weight: 600; }
    mat-card { margin-bottom: 16px; }
    .chart { height: 380px; }
    .fill { display: grid; grid-template-columns: 140px 1fr 100px; gap: 12px; align-items: center; margin: 8px 0; }
  `,
})
export class EventInsightsComponent implements OnInit {
  private readonly api = inject(DashboardApiService);
  readonly id = input.required<string>();
  readonly data = signal<Insights | null>(null);
  readonly missing = signal(false);

  readonly curve = computed(() => (this.data() ? salesCurve(this.data()!.insights) : {}));
  readonly velocity = computed(() => (this.data() ? velocityWithPrices(this.data()!.insights) : {}));

  ngOnInit(): void {
    this.api.insights(this.id()).subscribe({ next: (d) => this.data.set(d), error: () => this.missing.set(true) });
  }
}

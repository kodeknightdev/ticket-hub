import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { FilterBarComponent } from './shared/filter-bar.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatSidenavModule, MatListModule, MatButtonModule, FilterBarComponent],
  template: `
    <mat-toolbar color="primary" class="no-print">
      <span class="brand">ticket-hub insights</span>
      <span class="spacer"></span>
      @if (auth.user(); as user) {
        <span class="who">{{ user.name }} · {{ user.role }}{{ user.role === 'Organizer' ? ' (your events only)' : '' }}</span>
        <button mat-stroked-button (click)="logout()">Sign out</button>
      }
    </mat-toolbar>
    @if (auth.isAuthenticated()) {
      <mat-sidenav-container class="shell">
        <mat-sidenav mode="side" opened class="no-print">
          <mat-nav-list>
            @for (link of links; track link.path) {
              <a mat-list-item [routerLink]="link.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: link.path === '/' }">{{ link.label }}</a>
            }
          </mat-nav-list>
        </mat-sidenav>
        <mat-sidenav-content class="content">
          <app-filter-bar class="no-print" />
          <router-outlet />
        </mat-sidenav-content>
      </mat-sidenav-container>
    } @else {
      <router-outlet />
    }
  `,
  styles: `
    .brand { font-weight: 700; }
    .spacer { flex: 1; }
    .who { font-size: 14px; margin-right: 12px; opacity: 0.85; }
    .shell { min-height: calc(100vh - 64px); }
    mat-sidenav { width: 200px; }
    .content { padding: 16px 24px; background: #f5f6fa; }
    a.active { background: rgba(57, 73, 171, 0.1); }
    @media print { .no-print { display: none !important; } }
  `,
})
export class AppComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly links = [
    { path: '/', label: 'Overview' },
    { path: '/sales', label: 'Sales & breakdowns' },
    { path: '/events', label: 'Event performance' },
    { path: '/forecast', label: 'Forecasts' },
    { path: '/demand', label: 'Demand patterns' },
    { path: '/funnel', label: 'Funnel & pricing' },
    { path: '/alerts', label: 'Alerts' },
    { path: '/report', label: 'Printable report' },
  ];

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}

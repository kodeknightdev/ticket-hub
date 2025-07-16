import { Routes } from '@angular/router';
import { signedIn } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login.component').then((m) => m.LoginComponent), title: 'Sign in' },
  {
    path: '',
    canActivate: [signedIn],
    children: [
      { path: '', loadComponent: () => import('./pages/overview.component').then((m) => m.OverviewComponent), title: 'Overview' },
      { path: 'sales', loadComponent: () => import('./pages/sales.component').then((m) => m.SalesComponent), title: 'Sales' },
      { path: 'events', loadComponent: () => import('./pages/events.component').then((m) => m.EventsComponent), title: 'Events' },
      { path: 'events/:id', loadComponent: () => import('./pages/event-insights.component').then((m) => m.EventInsightsComponent), title: 'Event insights' },
      { path: 'forecast', loadComponent: () => import('./pages/forecast.component').then((m) => m.ForecastComponent), title: 'Forecast' },
      { path: 'demand', loadComponent: () => import('./pages/demand.component').then((m) => m.DemandComponent), title: 'Demand' },
      { path: 'funnel', loadComponent: () => import('./pages/funnel.component').then((m) => m.FunnelComponent), title: 'Funnel & pricing' },
      { path: 'alerts', loadComponent: () => import('./pages/alerts.component').then((m) => m.AlertsComponent), title: 'Alerts' },
      { path: 'report', loadComponent: () => import('./pages/report.component').then((m) => m.ReportComponent), title: 'Report' },
    ],
  },
  { path: '**', redirectTo: '' },
];

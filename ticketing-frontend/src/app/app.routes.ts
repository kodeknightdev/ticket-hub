import { Routes } from '@angular/router';
import { requireRole } from './core/auth.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/events/event-list.component').then((m) => m.EventListComponent), title: 'Events' },
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent), title: 'Sign in' },
  { path: 'events/:id', loadComponent: () => import('./features/events/event-page.component').then((m) => m.EventPageComponent) },
  {
    path: 'checkout/:holdId',
    canActivate: [requireRole()],
    loadComponent: () => import('./features/checkout/checkout.component').then((m) => m.CheckoutComponent),
    title: 'Checkout',
  },
  {
    path: 'tickets',
    canActivate: [requireRole()],
    loadComponent: () => import('./features/tickets/my-tickets.component').then((m) => m.MyTicketsComponent),
    title: 'My tickets',
  },
  {
    path: 'tickets/:id',
    canActivate: [requireRole()],
    loadComponent: () => import('./features/tickets/ticket-detail.component').then((m) => m.TicketDetailComponent),
    title: 'Ticket',
  },
  {
    path: 'organizer',
    canActivate: [requireRole('Organizer')],
    loadComponent: () => import('./features/organizer/organizer-events.component').then((m) => m.OrganizerEventsComponent),
    title: 'My events',
  },
  {
    path: 'organizer/new',
    canActivate: [requireRole('Organizer')],
    loadComponent: () => import('./features/organizer/event-editor.component').then((m) => m.EventEditorComponent),
    title: 'New event',
  },
  {
    path: 'scan',
    canActivate: [requireRole('Staff', 'Organizer')],
    loadComponent: () => import('./features/scanner/scanner.component').then((m) => m.ScannerComponent),
    title: 'Door scanner',
  },
  { path: '**', redirectTo: '' },
];

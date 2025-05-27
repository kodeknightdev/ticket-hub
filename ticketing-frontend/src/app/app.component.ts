import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule],
  template: `
    <mat-toolbar color="primary" class="toolbar">
      <a routerLink="/" class="brand">ticket-hub</a>
      <nav>
        <a mat-button routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Events</a>
        @if (auth.isAuthenticated()) {
          <a mat-button routerLink="/tickets" routerLinkActive="active">My tickets</a>
        }
        @if (auth.hasRole('Organizer')) {
          <a mat-button routerLink="/organizer" routerLinkActive="active">Organise</a>
        }
        @if (auth.hasRole('Staff', 'Organizer')) {
          <a mat-button routerLink="/scan" routerLinkActive="active">Scan</a>
        }
      </nav>
      <span class="spacer"></span>
      @if (auth.user(); as user) {
        <span class="who">{{ user.name }} · {{ user.role }}</span>
        <button mat-stroked-button (click)="logout()">Sign out</button>
      } @else {
        <a mat-stroked-button routerLink="/login">Sign in</a>
      }
    </mat-toolbar>
    <main class="content">
      <router-outlet />
    </main>
  `,
  styles: `
    .toolbar { position: sticky; top: 0; z-index: 10; gap: 8px; }
    .brand { color: inherit; text-decoration: none; font-weight: 700; margin-right: 16px; }
    nav a.active { background: rgba(255, 255, 255, 0.15); }
    .spacer { flex: 1; }
    .who { font-size: 14px; margin-right: 12px; opacity: 0.85; }
    .content { max-width: 1200px; margin: 0 auto; padding: 24px 16px; }
  `,
})
export class AppComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }
}

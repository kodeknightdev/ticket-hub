import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <mat-card class="login">
      <mat-card-header><mat-card-title>Sign in</mat-card-title></mat-card-header>
      <mat-card-content>
        <form (ngSubmit)="submit()" #form="ngForm">
          <mat-form-field appearance="outline">
            <mat-label>Email</mat-label>
            <input matInput name="email" type="email" [(ngModel)]="email" required autocomplete="username" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Password</mat-label>
            <input matInput name="password" type="password" [(ngModel)]="password" required autocomplete="current-password" />
          </mat-form-field>
          @if (error()) {
            <p class="error">{{ error() }}</p>
          }
          <button mat-flat-button color="primary" type="submit" [disabled]="busy() || form.invalid">Sign in</button>
        </form>
        <p class="hint">Demo accounts: buyer, organizer or staff &#64;tickethub.local</p>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .login { max-width: 420px; margin: 40px auto; }
    form { display: flex; flex-direction: column; gap: 4px; margin-top: 16px; }
    .error { color: #c62828; }
    .hint { color: #666; font-size: 13px; margin-top: 16px; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>('/');
  email = '';
  password = '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.email, this.password);
      await this.router.navigateByUrl(this.returnUrl() || '/');
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 401 ? 'Wrong email or password' : 'Sign in failed, try again');
    } finally {
      this.busy.set(false);
    }
  }
}

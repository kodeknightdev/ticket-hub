import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Role, SessionInfo, User } from './models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly user = signal<User | null>(null);
  readonly loaded = signal(false);
  readonly isAuthenticated = computed(() => this.user() !== null);

  async load(): Promise<void> {
    try {
      const session = await firstValueFrom(this.http.get<SessionInfo>('/bff/user'));
      this.user.set(session.user);
    } catch {
      this.user.set(null);
    } finally {
      this.loaded.set(true);
    }
  }

  async login(email: string, password: string): Promise<User> {
    const user = await firstValueFrom(this.http.post<User>('/bff/login', { email, password }));
    this.user.set(user);
    return user;
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/bff/logout', {}));
    this.user.set(null);
  }

  hasRole(...roles: Role[]): boolean {
    const user = this.user();
    return !!user && (roles.includes(user.role) || user.role === 'Admin');
  }
}

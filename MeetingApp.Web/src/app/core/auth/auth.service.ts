import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { map, Observable, tap } from 'rxjs';
import { getBrowserTimeZone } from './browser-time-zone';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  userId: number;
  firstName: string;
  lastName: string;
}

export interface UserProfile {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  timeZoneId: string | null;
}

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  password: string;
  profileImage: File | null;
}

export interface RegisterResponse {
  message: string;
  userId: number;
}

const storageKey = 'meetingapp.auth';

function isValidSession(value: unknown): value is LoginResponse {
  if (typeof value !== 'object' || value === null) return false;
  const session = value as Partial<LoginResponse>;
  return typeof session.accessToken === 'string' && session.accessToken.length > 0
    && typeof session.expiresAtUtc === 'string'
    && Date.parse(session.expiresAtUtc) > Date.now()
    && typeof session.userId === 'number' && Number.isInteger(session.userId) && session.userId > 0
    && typeof session.firstName === 'string' && typeof session.lastName === 'string';
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<LoginResponse | null>(this.restoreSession());
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly user = computed(() => {
    const session = this.session();
    return session ? {
      userId: session.userId,
      firstName: session.firstName,
      lastName: session.lastName,
      expiresAtUtc: session.expiresAtUtc
    } : null;
  });

  constructor() {
    this.scheduleExpiry();
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<unknown>('/api/auth/login', {
      email: request.email.trim(),
      password: request.password,
      timeZoneId: getBrowserTimeZone()
    }).pipe(
      map(response => {
        if (!isValidSession(response)) throw new Error('Geçerli oturum bilgisi alınamadı.');
        return response;
      }),
      tap(response => {
        this.session.set(response);
        try {
          sessionStorage.setItem(storageKey, JSON.stringify(response));
        } catch {
          // Depolama kullanılamıyorsa oturum bu sayfa açıkken bellekte tutulur.
        }
        this.scheduleExpiry();
      })
    );
  }

  register(request: RegisterRequest): Observable<RegisterResponse> {
    const data = new FormData();
    data.append('FirstName', request.firstName.trim());
    data.append('LastName', request.lastName.trim());
    data.append('Email', request.email.trim());
    data.append('PhoneNumber', request.phoneNumber.trim());
    data.append('Password', request.password);
    const timeZoneId = getBrowserTimeZone();
    if (timeZoneId) data.append('TimeZoneId', timeZoneId);
    if (request.profileImage) {
      data.append('ProfileImage', request.profileImage, request.profileImage.name);
    }

    return this.http.post<unknown>('/api/auth/register', data).pipe(
      map(response => {
        if (typeof response !== 'object' || response === null) {
          throw new Error('Geçerli kayıt yanıtı alınamadı.');
        }
        const result = response as Partial<RegisterResponse>;
        if (typeof result.message !== 'string' || typeof result.userId !== 'number'
          || !Number.isInteger(result.userId) || result.userId <= 0) {
          throw new Error('Geçerli kayıt yanıtı alınamadı.');
        }
        return result as RegisterResponse;
      })
    );
  }

  loadProfile(): Observable<UserProfile> {
    return this.http.get<UserProfile>('/api/auth/me');
  }

  updateTimeZone(timeZoneId: string | null): Observable<UserProfile> {
    return this.http.put<UserProfile>('/api/auth/me/time-zone', {
      timeZoneId: timeZoneId?.trim() || null
    });
  }

  getAccessToken(): string | null {
    const session = this.session();
    if (!session) return null;
    if (Date.parse(session.expiresAtUtc) <= Date.now()) {
      this.clearSession();
      return null;
    }
    return session.accessToken;
  }

  isAuthenticated(): boolean {
    return this.getAccessToken() !== null;
  }

  clearSession(): void {
    if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.session.set(null);
    try {
      sessionStorage.removeItem(storageKey);
    } catch {
      // Bellekteki oturum yine de kapatılır.
    }
  }

  logout(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  private restoreSession(): LoginResponse | null {
    try {
      const stored = sessionStorage.getItem(storageKey);
      if (!stored) return null;
      const session: unknown = JSON.parse(stored);
      if (isValidSession(session)) return session;
      sessionStorage.removeItem(storageKey);
    } catch {
      try { sessionStorage.removeItem(storageKey); } catch { }
    }
    return null;
  }

  private scheduleExpiry(): void {
    if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    const session = this.session();
    if (!session) return;
    const delay = Math.min(2_147_483_647, Math.max(0, Date.parse(session.expiresAtUtc) - Date.now()));
    this.expiryTimer = setTimeout(() => this.logout(), delay);
  }
}

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService, LoginResponse } from './auth.service';

describe('AuthService', () => {
  const storageKey = 'meetingapp.auth';
  let http: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  let service: AuthService | undefined;

  const validSession = (lifetime = 60_000): LoginResponse => ({
    accessToken: 'test.jwt.token',
    expiresAtUtc: new Date(Date.now() + lifetime).toISOString(),
    userId: 7,
    firstName: 'Lale',
    lastName: 'Deneme'
  });

  const auth = (): AuthService => service ??= TestBed.inject(AuthService);

  beforeEach(() => {
    sessionStorage.removeItem(storageKey);
    service = undefined;
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router }
      ]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    service?.clearSession();
    sessionStorage.removeItem(storageKey);
    http.verify();
  });

  it('trims the email while preserving every password character and stores a successful session', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'Europe/Rome' })
    } as Intl.DateTimeFormat);
    const result = validSession();
    let received: LoginResponse | undefined;
    auth().login({ email: '  lale@example.test  ', password: '  Parola  ' })
      .subscribe(value => received = value);

    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'lale@example.test', password: '  Parola  ', timeZoneId: 'Europe/Rome' });
    request.flush(result);

    expect(received).toEqual(result);
    expect(auth().getAccessToken()).toBe(result.accessToken);
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual(result);
    expect(auth().user()).toEqual({
      userId: result.userId,
      firstName: result.firstName,
      lastName: result.lastName,
      expiresAtUtc: result.expiresAtUtc
    });
    expect('accessToken' in auth().user()!).toBeFalse();
  });

  it('restores a still-valid session after the application is recreated', () => {
    const result = validSession();
    sessionStorage.setItem(storageKey, JSON.stringify(result));
    expect(auth().isAuthenticated()).toBeTrue();
    expect(auth().getAccessToken()).toBe(result.accessToken);
  });

  for (const invalidStoredValue of [
    'not valid JSON',
    JSON.stringify({ accessToken: '', expiresAtUtc: '2099-01-01T00:00:00Z', userId: 7 }),
    JSON.stringify({ accessToken: 'test.jwt.token', expiresAtUtc: 'not a date', userId: 7 })
  ]) {
    it('discards malformed stored session data without breaking application startup', () => {
      sessionStorage.setItem(storageKey, invalidStoredValue);
      expect(auth().isAuthenticated()).toBeFalse();
      expect(auth().user()).toBeNull();
      expect(sessionStorage.getItem(storageKey)).toBeNull();
    });
  }

  it('discards an expired stored token', () => {
    sessionStorage.setItem(storageKey, JSON.stringify(validSession(-1)));
    expect(auth().getAccessToken()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('propagates rejected credentials without creating a session', () => {
    let status: number | undefined;
    auth().login({ email: 'lale@example.test', password: 'wrong' })
      .subscribe({ error: error => status = error.status });
    http.expectOne('/api/auth/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
    expect(auth().isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('rejects an incomplete successful API response instead of accepting a broken session', () => {
    let rejected = false;
    auth().login({ email: 'lale@example.test', password: 'Parola' })
      .subscribe({ error: () => rejected = true });
    http.expectOne('/api/auth/login').flush({ accessToken: 'test.jwt.token' });
    expect(rejected).toBeTrue();
    expect(auth().getAccessToken()).toBeNull();
  });

  it('keeps the active session in memory if browser storage cannot be written', () => {
    spyOn(Storage.prototype, 'setItem').and.throwError('Storage blocked');
    auth().login({ email: 'lale@example.test', password: 'Parola' }).subscribe();
    http.expectOne('/api/auth/login').flush(validSession());
    expect(auth().isAuthenticated()).toBeTrue();
  });

  it('clears the session and returns to login when the token lifetime ends', fakeAsync(() => {
    sessionStorage.setItem(storageKey, JSON.stringify(validSession(1_000)));
    expect(auth().isAuthenticated()).toBeTrue();
    tick(1_001);
    expect(auth().isAuthenticated()).toBeFalse();
    expect(auth().user()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  }));

  it('loads the current user profile from the protected API endpoint', () => {
    const profile = { id: 7, firstName: 'Lale', lastName: 'Deneme', email: 'lale@example.test', phoneNumber: '+905550000007', timeZoneId: 'Europe/Rome' };
    let received: unknown;
    auth().loadProfile().subscribe(value => received = value);
    const request = http.expectOne('/api/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(profile);
    expect(received).toEqual(profile);
  });

  it('logs out by removing the token and returning to the login page', () => {
    sessionStorage.setItem(storageKey, JSON.stringify(validSession()));
    auth().logout();
    expect(auth().user()).toBeNull();
    expect(auth().getAccessToken()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});

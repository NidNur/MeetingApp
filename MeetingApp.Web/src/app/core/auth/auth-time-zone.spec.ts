import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, UserProfile } from './auth.service';
import { getBrowserTimeZone } from './browser-time-zone';

describe('Browser time zone and profile transport', () => {
  let auth: AuthService;
  let backend: HttpTestingController;
  const profile: UserProfile = {
    id: 7, firstName: 'Lale', lastName: 'Deneme', email: 'lale@example.test',
    phoneNumber: '+905550000007', timeZoneId: 'Europe/Rome'
  };
  const registration = {
    firstName: 'Lale', lastName: 'Deneme', email: 'lale@example.test',
    phoneNumber: '+905550000007', password: 'ParolaDenemesi2026!', profileImage: null
  };

  beforeEach(() => {
    sessionStorage.removeItem('meetingapp.auth');
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    auth.clearSession();
    backend.verify();
  });

  function browserZone(timeZone: string): void {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone })
    } as Intl.DateTimeFormat);
  }

  it('adds the browser IANA time zone to multipart registration', () => {
    browserZone('Europe/Rome');
    auth.register(registration).subscribe();
    const request = backend.expectOne('/api/auth/register');
    expect((request.request.body as FormData).get('TimeZoneId')).toBe('Europe/Rome');
    request.flush({ message: 'Created', userId: 7 });
  });

  it('adds the browser IANA time zone to login without changing credentials', () => {
    browserZone('Europe/Istanbul');
    auth.login({ email: ' lale@example.test ', password: ' unchanged ' }).subscribe({ error: () => {} });
    const request = backend.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({
      email: 'lale@example.test', password: ' unchanged ', timeZoneId: 'Europe/Istanbul'
    });
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('keeps login available when browser time zone detection throws', () => {
    spyOn(Intl, 'DateTimeFormat').and.throwError('Intl unavailable');
    auth.login({ email: 'lale@example.test', password: 'Parola' }).subscribe({ error: () => {} });
    const request = backend.expectOne('/api/auth/login');
    expect(request.request.body.timeZoneId).toBeNull();
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('reads the browser zone again on each login after it changes', () => {
    const formatter = spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'Europe/Rome' })
    } as Intl.DateTimeFormat);
    const session = {
      accessToken: 'test-token', expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
      userId: 7, firstName: 'Lale', lastName: 'Deneme'
    };

    auth.login({ email: 'lale@example.test', password: 'Parola' }).subscribe();
    const firstLogin = backend.expectOne('/api/auth/login');
    expect(firstLogin.request.body.timeZoneId).toBe('Europe/Rome');
    firstLogin.flush(session);
    auth.clearSession();

    formatter.and.returnValue({
      resolvedOptions: () => ({ timeZone: 'Europe/Istanbul' })
    } as Intl.DateTimeFormat);
    auth.login({ email: 'lale@example.test', password: 'Parola' }).subscribe();
    const nextLogin = backend.expectOne('/api/auth/login');
    expect(nextLogin.request.body.timeZoneId).toBe('Europe/Istanbul');
    nextLogin.flush(session);
  });

  it('omits the optional registration field when detection is unavailable', () => {
    spyOn(Intl, 'DateTimeFormat').and.throwError('Intl unavailable');
    auth.register(registration).subscribe();
    const request = backend.expectOne('/api/auth/register');
    expect((request.request.body as FormData).has('TimeZoneId')).toBeFalse();
    request.flush({ message: 'Created', userId: 7 });
  });

  it('returns null rather than guessing a region when the browser returns an empty zone', () => {
    browserZone('');
    expect(getBrowserTimeZone()).toBeNull();
  });

  it('sends a trimmed time zone preference and returns the updated profile', () => {
    let received: UserProfile | undefined;
    auth.updateTimeZone(' Europe/Rome ').subscribe(value => received = value);
    const request = backend.expectOne('/api/auth/me/time-zone');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ timeZoneId: 'Europe/Rome' });
    request.flush(profile);
    expect(received).toEqual(profile);
  });

  it('sends null when the preference is cleared instead of an invalid empty identifier', () => {
    auth.updateTimeZone('   ').subscribe();
    const request = backend.expectOne('/api/auth/me/time-zone');
    expect(request.request.body).toEqual({ timeZoneId: null });
    request.flush({ ...profile, timeZoneId: null });
  });
});

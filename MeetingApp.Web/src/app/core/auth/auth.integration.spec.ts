import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('Authenticated requests and protected routes', () => {
  const storageKey = 'meetingapp.auth';
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    sessionStorage.setItem(storageKey, JSON.stringify({
      accessToken: 'test.jwt.token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 7,
      firstName: 'Lale',
      lastName: 'Deneme'
    }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
  });

  afterEach(() => {
    auth.clearSession();
    backend.verify();
  });

  it('attaches the bearer token to a protected relative API request', () => {
    http.get('/api/auth/me').subscribe();
    const request = backend.expectOne('/api/auth/me');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test.jwt.token');
    request.flush({});
  });

  it('keeps existing headers when adding bearer authentication', () => {
    http.get('/api/meetings', { headers: { 'X-Request-Id': 'fixture' } }).subscribe();
    const request = backend.expectOne('/api/meetings');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test.jwt.token');
    expect(request.request.headers.get('X-Request-Id')).toBe('fixture');
    request.flush({});
  });

  for (const url of [
    '/api/auth/login',
    '/api/auth/register',
    '/api/auth/login?source=test',
    '/assets/example.json',
    '/api-other/example',
    'https://external.example.test/api/meetings',
    '//external.example.test/api/meetings',
    'https://localhost:7085/api/auth/me'
  ]) {
    it(`does not expose the token to public authentication or non-API URL ${url}`, () => {
      http.get(url).subscribe();
      const request = backend.expectOne(url);
      expect(request.request.headers.has('Authorization')).toBeFalse();
      request.flush({});
    });
  }

  it('sends a request without an Authorization header when no session exists', () => {
    auth.clearSession();
    http.get('/api/auth/me').subscribe();
    const request = backend.expectOne('/api/auth/me');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('clears rejected authentication and redirects after a protected API returns401', () => {
    let status: number | undefined;
    http.get('/api/auth/me').subscribe({ error: error => status = error.status });
    backend.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
    expect(auth.user()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('preserves a newer login when an older protected request returns401 late', () => {
    let oldRequestStatus: number | undefined;
    http.get('/api/auth/me').subscribe({ error: error => oldRequestStatus = error.status });
    const oldRequest = backend.expectOne('/api/auth/me');
    expect(oldRequest.request.headers.get('Authorization')).toBe('Bearer test.jwt.token');

    auth.login({ email: 'lale@example.test', password: 'Parola' }).subscribe();
    backend.expectOne('/api/auth/login').flush({
      accessToken: 'new.jwt.token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 7,
      firstName: 'Lale',
      lastName: 'Deneme'
    });

    oldRequest.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(oldRequestStatus).toBe(401);
    expect(auth.getAccessToken()).toBe('new.jwt.token');
    expect(auth.isAuthenticated()).toBeTrue();
    expect(JSON.parse(sessionStorage.getItem(storageKey)!).accessToken).toBe('new.jwt.token');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('retains a valid session when the API denies a particular operation with403', () => {
    http.get('/api/meetings').subscribe({ error: () => { } });
    backend.expectOne('/api/meetings').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(auth.isAuthenticated()).toBeTrue();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('lets rejected login credentials stay on the form without a global redirect', () => {
    auth.clearSession();
    auth.login({ email: 'lale@example.test', password: 'wrong' }).subscribe({ error: () => { } });
    const request = backend.expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isAuthenticated()).toBeFalse();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('allows a protected route for a valid session', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
    expect(result).toBeTrue();
  });

  it('redirects a guest to login before activating a protected route', () => {
    auth.clearSession();
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
    expect(result instanceof UrlTree).toBeTrue();
    expect(router.serializeUrl(result as UrlTree)).toBe('/login');
  });
});

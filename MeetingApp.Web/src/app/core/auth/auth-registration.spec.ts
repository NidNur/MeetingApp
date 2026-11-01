import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService, LoginResponse, RegisterRequest, RegisterResponse } from './auth.service';

describe('Registration transport', () => {
  const storageKey = 'meetingapp.auth';
  let backend: HttpTestingController;
  let auth: AuthService | undefined;
  let router: Router;

  const service = (): AuthService => auth ??= TestBed.inject(AuthService);
  const form = (): RegisterRequest => ({
    firstName: '  Lale  ',
    lastName: '  Deneme  ',
    email: '  lale@example.test  ',
    phoneNumber: '  +905550000007  ',
    password: '  ParolaDenemesi2026!  ',
    profileImage: null
  });
  const existingSession = (): LoginResponse => ({
    accessToken: 'existing.jwt.token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    userId: 7,
    firstName: 'Önceki',
    lastName: 'Kullanıcı'
  });

  beforeEach(() => {
    sessionStorage.removeItem(storageKey);
    auth = undefined;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
  });

  afterEach(() => {
    auth?.clearSession();
    sessionStorage.removeItem(storageKey);
    backend.verify();
  });

  it('sends multipart fields using API names, trims text, and preserves the password exactly', () => {
    let result: RegisterResponse | undefined;
    service().register(form()).subscribe(value => result = value);

    const request = backend.expectOne('/api/auth/register');
    const data = request.request.body as FormData;
    expect(request.request.method).toBe('POST');
    expect(data instanceof FormData).toBeTrue();
    expect(data.get('FirstName')).toBe('Lale');
    expect(data.get('LastName')).toBe('Deneme');
    expect(data.get('Email')).toBe('lale@example.test');
    expect(data.get('PhoneNumber')).toBe('+905550000007');
    expect(data.get('Password')).toBe('  ParolaDenemesi2026!  ');
    expect(data.has('ConfirmPassword')).toBeFalse();
    expect(data.has('ProfileImage')).toBeFalse();
    expect(request.request.headers.has('Content-Type')).toBeFalse();
    request.flush({ message: 'Kullanıcı kaydı oluşturuldu.', userId: 12 });

    expect(result?.userId).toBe(12);
    expect(service().isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('includes the selected file with its original filename and unchanged bytes', async () => {
    const image = new File(['unchanged-image-fixture'], 'profil.png', { type: 'image/png' });
    service().register({ ...form(), profileImage: image }).subscribe();
    const request = backend.expectOne('/api/auth/register');
    const upload = (request.request.body as FormData).get('ProfileImage') as File;

    expect(upload instanceof File).toBeTrue();
    expect(upload.name).toBe('profil.png');
    expect(upload.type).toBe('image/png');
    expect(upload.size).toBe(image.size);
    expect(await upload.text()).toBe('unchanged-image-fixture');
    expect(request.request.headers.has('Content-Type')).toBeFalse();
    request.flush({ message: 'Kullanıcı kaydı oluşturuldu.', userId: 12 });
  });

  it('does not attach an existing bearer token or replace that session after registration', () => {
    const current = existingSession();
    sessionStorage.setItem(storageKey, JSON.stringify(current));
    expect(service().getAccessToken()).toBe(current.accessToken);

    service().register(form()).subscribe();
    const request = backend.expectOne('/api/auth/register');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({ message: 'Kullanıcı kaydı oluşturuldu.', userId: 12 });

    expect(service().getAccessToken()).toBe(current.accessToken);
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual(current);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('propagates duplicate-email rejection without logging out an existing session', () => {
    const current = existingSession();
    sessionStorage.setItem(storageKey, JSON.stringify(current));
    let status: number | undefined;
    service().register(form()).subscribe({ error: error => status = error.status });
    backend.expectOne('/api/auth/register').flush(
      { message: 'Bu e-posta adresi zaten kayıtlı.' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
    expect(service().getAccessToken()).toBe(current.accessToken);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('rejects an incomplete success response instead of reporting a completed registration', () => {
    let rejected = false;
    service().register(form()).subscribe({ error: () => rejected = true });
    backend.expectOne('/api/auth/register').flush({ message: 'Created' });

    expect(rejected).toBeTrue();
    expect(service().isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
});

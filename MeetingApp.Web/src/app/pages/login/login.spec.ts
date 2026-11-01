import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Login } from './login';

describe('Login form', () => {
  let fixture: ComponentFixture<Login>;
  let backend: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  let destroyed: boolean;

  const response = () => ({
    accessToken: 'test.jwt.token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    userId: 7,
    firstName: 'Lale',
    lastName: 'Deneme'
  });

  beforeEach(async () => {
    sessionStorage.removeItem('meetingapp.auth');
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
    fixture = TestBed.createComponent(Login);
    destroyed = false;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (!destroyed) fixture.destroy();
    auth.clearSession();
    backend.verify();
  });

  function setValidForm(): void {
    fixture.componentInstance.form.setValue({ email: 'lale@example.test', password: 'Parola' });
  }

  it('shows required-field feedback without sending an empty login request', () => {
    fixture.componentInstance.submit();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('#email-error')?.textContent).toContain('E-posta adresinizi girin.');
    expect(element.querySelector('#password-error')?.textContent).toContain('Parolanızı girin.');
    expect(element.querySelector('#email')?.getAttribute('aria-invalid')).toBe('true');
    expect(element.querySelector('#email')?.getAttribute('aria-describedby')).toBe('email-error');
    backend.expectNone('/api/auth/login');
  });

  it('rejects malformed email before contacting the server', () => {
    fixture.componentInstance.form.setValue({ email: 'not-an-email', password: 'Parola' });
    fixture.componentInstance.submit();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('#email-error')?.textContent)
      .toContain('Geçerli bir e-posta adresi girin.');
    backend.expectNone('/api/auth/login');
  });

  it('enforces the API password length limit before submitting', () => {
    fixture.componentInstance.form.setValue({ email: 'lale@example.test', password: 'a'.repeat(129) });
    fixture.componentInstance.submit();
    expect(fixture.componentInstance.form.controls.password.hasError('maxlength')).toBeTrue();
    backend.expectNone('/api/auth/login');
  });

  it('normalizes email whitespace without changing the password', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'Europe/Rome' })
    } as Intl.DateTimeFormat);
    fixture.componentInstance.form.setValue({ email: '  lale@example.test  ', password: '  Parola  ' });
    fixture.componentInstance.submit();
    const request = backend.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ email: 'lale@example.test', password: '  Parola  ', timeZoneId: 'Europe/Rome' });
    request.flush(response());
  });

  it('uses login validation rather than imposing the registration minimum password length', () => {
    fixture.componentInstance.form.setValue({ email: 'lale@example.test', password: 'x' });
    fixture.componentInstance.submit();
    const request = backend.expectOne('/api/auth/login');
    expect(request.request.body.password).toBe('x');
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  it('prevents duplicate submission and protects fields while a login request is pending', () => {
    setValidForm();
    fixture.componentInstance.submit();
    const request = backend.expectOne('/api/auth/login');
    fixture.componentInstance.submit();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((element.querySelector('#email') as HTMLInputElement).readOnly).toBeTrue();
    expect((element.querySelector('#password') as HTMLInputElement).readOnly).toBeTrue();
    expect(element.querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    backend.expectNone('/api/auth/login');

    request.flush(response());
    fixture.detectChanges();
    expect(fixture.componentInstance.submitting()).toBeFalse();
    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('shows a fixed401 message without exposing raw server details and allows retry', () => {
    setValidForm();
    fixture.componentInstance.submit();
    backend.expectOne('/api/auth/login').flush(
      { message: 'private-server-detail' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('E-posta veya parola hatalı.');
    expect(element.textContent).not.toContain('private-server-detail');
    expect(fixture.componentInstance.submitting()).toBeFalse();
    expect(auth.isAuthenticated()).toBeFalse();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('reports an unreachable API with a useful retry message', () => {
    setValidForm();
    fixture.componentInstance.submit();
    backend.expectOne('/api/auth/login').error(new ProgressEvent('network-error'));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent)
      .toContain('Şu anda giriş hizmetine ulaşılamıyor.');
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });

  it('clears the password and opens the meeting list after successful authentication', () => {
    setValidForm();
    fixture.componentInstance.submit();
    backend.expectOne('/api/auth/login').flush(response());
    expect(auth.isAuthenticated()).toBeTrue();
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
    expect(router.navigate).toHaveBeenCalledWith(['/meetings'], { replaceUrl: true });
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('test.jwt.token');
  });

  it('can show and hide the password without submitting the form', () => {
    const element = fixture.nativeElement as HTMLElement;
    const button = element.querySelector('.visibility-button') as HTMLButtonElement;
    const password = element.querySelector('#password') as HTMLInputElement;
    expect(password.type).toBe('password');
    button.click();
    fixture.detectChanges();
    expect(password.type).toBe('text');
    expect(button.getAttribute('aria-label')).toBe('Parolayı gizle');
    expect(button.getAttribute('aria-pressed')).toBe('true');
    button.click();
    fixture.detectChanges();
    expect(password.type).toBe('password');
    backend.expectNone('/api/auth/login');
  });

  it('cancels an in-flight request when the login screen is destroyed', () => {
    setValidForm();
    fixture.componentInstance.submit();
    const request = backend.expectOne('/api/auth/login');
    fixture.destroy();
    destroyed = true;
    expect(request.cancelled).toBeTrue();
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, UserProfile } from '../../core/auth/auth.service';
import { Account } from './account';

describe('Account time zone preference', () => {
  let fixture: ComponentFixture<Account>;
  let backend: HttpTestingController;
  let auth: AuthService;
  const profile: UserProfile = {
    id: 7, firstName: 'Lale', lastName: 'Deneme', email: 'lale@example.test',
    phoneNumber: '+905550000007', timeZoneId: 'Europe/Rome'
  };

  beforeEach(async () => {
    sessionStorage.removeItem('meetingapp.auth');
    await TestBed.configureTestingModule({
      imports: [Account],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Account);
    fixture.detectChanges();
    backend.expectOne('/api/auth/me').flush(profile);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    auth.clearSession();
    backend.verify();
  });

  const page = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const input = (): HTMLInputElement => page().querySelector('#timeZoneId')!;
  function enter(value: string): void {
    input().value = value;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }
  function submit(): void {
    page().querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it('loads the stored preference without replacing it with the current browser zone', () => {
    expect(input().value).toBe('Europe/Rome');
    expect(page().querySelector('dl')?.textContent).toContain('Europe/Rome');
    backend.expectNone('/api/auth/me/time-zone');
  });

  it('fills the browser zone only when requested and waits for explicit save', () => {
    spyOn(Intl, 'DateTimeFormat').and.returnValue({
      resolvedOptions: () => ({ timeZone: 'Europe/Istanbul' })
    } as Intl.DateTimeFormat);
    (page().querySelector('button[type="button"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(input().value).toBe('Europe/Istanbul');
    expect(page().querySelector('dl')?.textContent).toContain('Europe/Rome');
    backend.expectNone('/api/auth/me/time-zone');
  });

  it('saves the preference, blocks duplicate requests, and updates the profile after success', () => {
    enter(' Asia/Tokyo ');
    submit();
    const request = backend.expectOne('/api/auth/me/time-zone');
    expect(request.request.body).toEqual({ timeZoneId: 'Asia/Tokyo' });
    expect(input().readOnly).toBeTrue();
    expect((page().querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
    submit();
    backend.expectNone('/api/auth/me/time-zone');
    request.flush({ ...profile, timeZoneId: 'Asia/Tokyo' });
    fixture.detectChanges();
    expect(input().readOnly).toBeFalse();
    expect(page().querySelector('dl')?.textContent).toContain('Asia/Tokyo');
    expect(page().querySelector('[role="status"]')?.textContent).toContain('Saat diliminiz kaydedildi');
  });

  it('shows a safe validation message and preserves the saved preference after a rejected zone', () => {
    enter('invalid/zone');
    submit();
    backend.expectOne('/api/auth/me/time-zone').flush({ errors: { TimeZoneId: ['private-server-detail'] } },
      { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Geçerli bir IANA saat dilimi girin');
    expect(page().textContent).not.toContain('private-server-detail');
    expect(page().querySelector('dl')?.textContent).toContain('Europe/Rome');
    expect(input().value).toBe('invalid/zone');
  });

  it('supports a cleared optional preference with UTC fallback', () => {
    enter('   ');
    submit();
    const request = backend.expectOne('/api/auth/me/time-zone');
    expect(request.request.body).toEqual({ timeZoneId: null });
    request.flush({ ...profile, timeZoneId: null });
    fixture.detectChanges();
    expect(input().value).toBe('');
    expect(page().querySelector('dl')?.textContent).toContain('UTC (saat dilimi belirtilmemiş)');
  });

  it('rejects an overlong preference before contacting the API', () => {
    enter('a'.repeat(101));
    submit();
    backend.expectNone('/api/auth/me/time-zone');
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('en fazla 100 karakter');
  });

  it('allows retry after a server error without displaying success', () => {
    enter('Europe/Istanbul');
    submit();
    backend.expectOne('/api/auth/me/time-zone').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Saat dilimi kaydedilemedi');
    expect(page().querySelector('[role="status"]')).toBeNull();
    submit();
    backend.expectOne('/api/auth/me/time-zone').flush({ ...profile, timeZoneId: 'Europe/Istanbul' });
    fixture.detectChanges();
    expect(page().querySelector('dl')?.textContent).toContain('Europe/Istanbul');
  });

  it('leaves a saved value intact when browser detection is unavailable', () => {
    spyOn(Intl, 'DateTimeFormat').and.throwError('Intl unavailable');
    (page().querySelector('button[type="button"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(input().value).toBe('Europe/Rome');
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('elle girebilirsiniz');
    backend.expectNone('/api/auth/me/time-zone');
  });

  it('clears stale save feedback when the user starts another edit', () => {
    enter('Europe/Istanbul');
    submit();
    backend.expectOne('/api/auth/me/time-zone').flush({ ...profile, timeZoneId: 'Europe/Istanbul' });
    fixture.detectChanges();
    expect(page().querySelector('[role="status"]')).not.toBeNull();
    enter('Europe/Rome');
    expect(page().querySelector('[role="status"]')).toBeNull();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, UserProfile } from '../../core/auth/auth.service';
import { Account } from './account';

describe('Account automatic time zone', () => {
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
  });

  afterEach(() => {
    fixture.destroy();
    auth.clearSession();
    backend.verify();
  });

  const page = (): HTMLElement => fixture.nativeElement as HTMLElement;
  function loadProfile(value: UserProfile = profile): void {
    backend.expectOne('/api/auth/me').flush(value);
    fixture.detectChanges();
  }

  it('shows the saved browser zone without a manual input or save request', () => {
    loadProfile();
    expect(page().querySelector('dl')?.textContent).toContain('Europe/Rome');
    expect(page().textContent).toContain('Saat diliminiz tarayıcınızdan otomatik alınır');
    expect(page().querySelector('input, select, form')).toBeNull();
    backend.expectNone('/api/auth/me/time-zone');
  });

  it('shows UTC when no browser zone has been saved', () => {
    loadProfile({ ...profile, timeZoneId: null });
    expect(page().querySelector('dl')?.textContent).toContain('UTC (saat dilimi belirlenemedi)');
    expect(page().querySelector('input, select, form')).toBeNull();
    backend.expectNone('/api/auth/me/time-zone');
  });

  it('shows loading status until the profile is available', () => {
    expect(page().querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    expect(page().querySelector('[role="status"]')?.textContent).toContain('yükleniyor');
    expect(page().querySelector('dl')).toBeNull();
    loadProfile();
    expect(page().querySelector('section')?.getAttribute('aria-busy')).toBe('false');
    expect(page().querySelector('[role="status"]')).toBeNull();
  });

  it('allows retry after a failed profile request without exposing server details', () => {
    backend.expectOne('/api/auth/me').flush({ detail: 'private-server-detail' },
      { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Hesap bilgileri alınamadı');
    expect(page().textContent).not.toContain('private-server-detail');
    (page().querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    loadProfile();
    expect(page().querySelector('[role="alert"]')).toBeNull();
    expect(page().querySelector('dl')?.textContent).toContain(profile.email);
  });

  it('does not show a misleading profile error for an expired session', () => {
    backend.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')).toBeNull();
    expect(page().querySelector('dl')).toBeNull();
    backend.expectNone('/api/auth/me/time-zone');
  });
});

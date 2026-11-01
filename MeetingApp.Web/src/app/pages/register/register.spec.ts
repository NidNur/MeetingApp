import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Register } from './register';

describe('Registration form', () => {
  let fixture: ComponentFixture<Register>;
  let backend: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  let destroyed: boolean;
  const password = 'ParolaDenemesi2026!';
  const registered = { message: 'Kullanıcı kaydı oluşturuldu.', userId: 12 };
  const pngBytes = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7mYAAAAASUVORK5CYII='), value => value.charCodeAt(0));

  beforeEach(async () => {
    sessionStorage.removeItem('meetingapp.auth');
    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
    fixture = TestBed.createComponent(Register);
    destroyed = false;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (!destroyed) fixture.destroy();
    auth.clearSession();
    backend.verify();
  });

  const page = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const component = (): Register => fixture.componentInstance;
  function fillValid(): void {
    component().form.setValue({
      firstName: 'Lale', lastName: 'Deneme', email: 'lale@example.test',
      phoneNumber: '+905550000007', password, confirmPassword: password
    });
  }
  function image(name = 'profil.png'): File {
    return new File([pngBytes.buffer as ArrayBuffer], name, { type: 'image/png' });
  }
  function selectFile(file: File | null): HTMLInputElement {
    const input = page().querySelector('input[type="file"]') as HTMLInputElement;
    const files = new DataTransfer();
    if (file) files.items.add(file);
    input.files = files.files;
    component().onProfileImageChange({ target: input } as unknown as Event);
    return input;
  }

  it('shows field-specific required messages and sends no empty registration', () => {
    component().submit();
    fixture.detectChanges();
    expect(page().querySelector('#firstName-error')?.textContent).toContain('Adınızı girin.');
    expect(page().querySelector('#email-error')?.textContent).toContain('E-posta adresinizi girin.');
    expect(page().querySelector('#password-error')?.textContent).toContain('Parolanızı girin.');
    expect(page().querySelector('#confirmPassword-error')?.textContent).toContain('Parolanızı tekrar girin.');
    expect(page().querySelector('#firstName')?.getAttribute('aria-describedby')).toBe('firstName-error');
    backend.expectNone('/api/auth/register');
  });

  it('rejects blank names even when they contain whitespace characters', () => {
    fillValid();
    component().form.patchValue({ firstName: '   ', lastName: '\t  ' });
    component().submit();
    expect(component().form.controls.firstName.hasError('required')).toBeTrue();
    expect(component().form.controls.lastName.hasError('required')).toBeTrue();
    backend.expectNone('/api/auth/register');
  });

  it('rejects malformed email and telephone values without making an API request', () => {
    fillValid();
    component().form.patchValue({ email: 'invalid-address', phoneNumber: 'not-a-phone' });
    component().submit();
    fixture.detectChanges();
    expect(component().form.controls.email.invalid).toBeTrue();
    expect(component().form.controls.phoneNumber.invalid).toBeTrue();
    expect(page().querySelector('#phoneNumber-error')?.textContent).toContain('Geçerli bir telefon numarası');
    backend.expectNone('/api/auth/register');
  });

  for (const length of [11, 129]) {
    it(`rejects a ${length}-character password outside the registration limits`, () => {
      fillValid();
      component().form.patchValue({ password: 'a'.repeat(length), confirmPassword: 'a'.repeat(length) });
      component().submit();
      expect(component().form.controls.password.invalid).toBeTrue();
      backend.expectNone('/api/auth/register');
    });
  }

  it('requires matching passwords and describes the mismatch beside confirmation', () => {
    fillValid();
    component().form.controls.confirmPassword.setValue('FarkliParola2026!');
    component().submit();
    fixture.detectChanges();
    expect(component().form.hasError('passwordMismatch')).toBeTrue();
    expect(page().querySelector('#confirmPassword-error')?.textContent).toContain('Parolalar aynı olmalıdır.');
    backend.expectNone('/api/auth/register');
  });

  it('trims names and contact fields while preserving password spaces and omitting confirmation', () => {
    fillValid();
    component().form.patchValue({
      firstName: '  Lale  ', lastName: '  Deneme  ', email: '  lale@example.test  ',
      phoneNumber: '  +905550000007  ', password: '  ParolaDenemesi2026!  ',
      confirmPassword: '  ParolaDenemesi2026!  '
    });
    component().submit();
    const request = backend.expectOne('/api/auth/register');
    const data = request.request.body as FormData;
    expect(data.get('FirstName')).toBe('Lale');
    expect(data.get('LastName')).toBe('Deneme');
    expect(data.get('PhoneNumber')).toBe('+905550000007');
    expect(data.get('Email')).toBe('lale@example.test');
    expect(data.get('Password')).toBe('  ParolaDenemesi2026!  ');
    expect(data.has('ConfirmPassword')).toBeFalse();
    request.flush(registered);
  });

  it('prevents double submission and disables file changes while the request is pending', () => {
    fillValid();
    component().submit();
    const request = backend.expectOne('/api/auth/register');
    component().submit();
    fixture.detectChanges();
    expect((page().querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((page().querySelector('#firstName') as HTMLInputElement).readOnly).toBeTrue();
    expect((page().querySelector('#password') as HTMLInputElement).readOnly).toBeTrue();
    expect((page().querySelector('input[type="file"]') as HTMLInputElement).disabled).toBeTrue();
    expect(page().querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    backend.expectNone('/api/auth/register');
    request.flush(registered);
    expect(component().submitting()).toBeFalse();
  });

  it('shows a safe duplicate-email message and keeps entered data available for correction', () => {
    fillValid();
    component().submit();
    backend.expectOne('/api/auth/register').flush(
      { message: 'private-server-detail' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(component().errorMessage()).toContain('Bu e-posta adresi zaten kayıtlı.');
    expect(page().querySelector('#email-error')?.textContent).toContain('Bu e-posta adresi zaten kayıtlı.');
    expect(page().textContent).not.toContain('private-server-detail');
    expect(component().form.controls.firstName.value).toBe('Lale');
    expect(component().submitting()).toBeFalse();
    component().form.controls.email.setValue('duzeltilen@example.test');
    expect(component().serverErrors().email).toBeUndefined();
  });

  it('maps known API validation fields to local messages and excludes raw error details', () => {
    fillValid();
    component().submit();
    backend.expectOne('/api/auth/register').flush({ errors: {
      'request.PhoneNumber': ['secret-phone-detail'], Password: ['secret-password-detail'],
      UnexpectedField: ['secret-unexpected-detail']
    } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(page().querySelector('#phoneNumber-error')?.textContent).toContain('Geçerli bir telefon numarası');
    expect(page().querySelector('#password-error')?.textContent).toContain('12 ile 128');
    expect(page().textContent).not.toContain('secret-');
    expect(component().submitting()).toBeFalse();
  });

  it('reports an unavailable registration API and permits a later retry', () => {
    fillValid();
    component().submit();
    backend.expectOne('/api/auth/register').error(new ProgressEvent('network-error'));
    expect(component().errorMessage()).toContain('kayıt hizmetine ulaşılamıyor');
    expect(component().submitting()).toBeFalse();
    component().submit();
    backend.expectOne('/api/auth/register').flush(registered);
    expect(component().success()).toBeTrue();
  });

  it('previews a valid image and releases replaced and removed preview URLs', () => {
    const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();
    const first = image('ilk.png');
    const second = image('ikinci.png');
    const input = selectFile(first);
    const firstUrl = component().profileImagePreview()!;
    expect(component().profileImage()).toBe(first);
    expect(firstUrl.startsWith('blob:')).toBeTrue();
    selectFile(second);
    const secondUrl = component().profileImagePreview()!;
    expect(revoke).toHaveBeenCalledWith(firstUrl);
    expect(component().profileImage()).toBe(second);
    component().removeProfileImage(input);
    expect(revoke).toHaveBeenCalledWith(secondUrl);
    expect(component().profileImage()).toBeNull();
    expect(component().profileImagePreview()).toBeNull();
    expect(input.value).toBe('');
  });

  for (const invalidFile of [
    () => new File(['svg-content'], 'avatar.svg', { type: 'image/svg+xml' }),
    () => new File([], 'empty.png', { type: 'image/png' }),
    () => new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' }),
    () => new File(['wrong-type'], 'mismatch.png', { type: 'image/jpeg' })
  ]) {
    it('blocks invalid profile image input without submitting a registration', () => {
      fillValid();
      selectFile(invalidFile());
      expect(component().profileImageError()).not.toBe('');
      expect(component().profileImage()).toBeNull();
      expect(component().profileImagePreview()).toBeNull();
      component().submit();
      backend.expectNone('/api/auth/register');
    });
  }

  it('lets the user remove an invalid optional image and register without a file', () => {
    fillValid();
    selectFile(new File(['svg-content'], 'avatar.svg', { type: 'image/svg+xml' }));
    fixture.detectChanges();
    const remove = page().querySelector('.profile-field button') as HTMLButtonElement | null;
    expect(remove).not.toBeNull();
    remove!.click();
    fixture.detectChanges();
    expect(component().profileImageError()).toBe('');
    component().submit();
    const request = backend.expectOne('/api/auth/register');
    expect((request.request.body as FormData).has('ProfileImage')).toBeFalse();
    request.flush(registered);
  });

  it('ignores a stale image load error but rejects an unreadable current preview', () => {
    const input = selectFile(image('ilk.png'));
    const oldUrl = component().profileImagePreview()!;
    const nextImage = image('ikinci.png');
    selectFile(nextImage);
    const currentUrl = component().profileImagePreview()!;
    const stale = document.createElement('img');
    stale.setAttribute('src', oldUrl);
    component().onImagePreviewError({ target: stale } as unknown as Event, input);
    expect(component().profileImage()).toBe(nextImage);
    const current = document.createElement('img');
    current.setAttribute('src', currentUrl);
    component().onImagePreviewError({ target: current } as unknown as Event, input);
    expect(component().profileImage()).toBeNull();
    expect(component().profileImagePreview()).toBeNull();
    expect(component().profileImageError()).toContain('görüntülenemiyor');
  });

  it('clears passwords and image data and offers login after success without automatic authentication', () => {
    fillValid();
    const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();
    selectFile(image());
    const previewUrl = component().profileImagePreview()!;
    component().submit();
    backend.expectOne('/api/auth/register').flush(registered);
    fixture.detectChanges();
    expect(component().success()).toBeTrue();
    expect(component().form.controls.password.value).toBe('');
    expect(component().form.controls.confirmPassword.value).toBe('');
    expect(component().profileImage()).toBeNull();
    expect(component().profileImagePreview()).toBeNull();
    expect(revoke).toHaveBeenCalledWith(previewUrl);
    expect(page().querySelector('[role="status"]')?.textContent).toContain('Kaydınız oluşturuldu');
    expect(page().querySelector('a[href="/login"]')?.getAttribute('href')).toBe('/login');
    expect(auth.isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem('meetingapp.auth')).toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('shows or hides both password fields without submitting', () => {
    const toggle = page().querySelector('.visibility-button') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    expect((page().querySelector('#password') as HTMLInputElement).type).toBe('text');
    expect((page().querySelector('#confirmPassword') as HTMLInputElement).type).toBe('text');
    toggle.click();
    fixture.detectChanges();
    expect((page().querySelector('#password') as HTMLInputElement).type).toBe('password');
    expect((page().querySelector('#confirmPassword') as HTMLInputElement).type).toBe('password');
    backend.expectNone('/api/auth/register');
  });

  it('releases the current preview and cancels a pending request when the page is destroyed', () => {
    fillValid();
    const revoke = spyOn(URL, 'revokeObjectURL').and.callThrough();
    selectFile(image());
    const previewUrl = component().profileImagePreview()!;
    component().submit();
    const request = backend.expectOne('/api/auth/register');
    fixture.destroy();
    destroyed = true;
    expect(request.cancelled).toBeTrue();
    expect(revoke).toHaveBeenCalledWith(previewUrl);
    expect(component().submitting()).toBeFalse();
  });
});

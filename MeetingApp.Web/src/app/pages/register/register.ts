import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnDestroy, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

type FormField = 'firstName' | 'lastName' | 'email' | 'phoneNumber' | 'password' | 'confirmPassword';
type RegistrationField = FormField | 'profileImage';

const passwordsMatch: ValidatorFn = control => {
  const password = control.get('password')?.value;
  const confirmation = control.get('confirmPassword')?.value;
  return password && confirmation && password !== confirmation ? { passwordMismatch: true } : null;
};

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.css'
})
export class Register implements OnDestroy {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private fileInput: HTMLInputElement | null = null;

  readonly submitting = signal(false);
  readonly passwordVisible = signal(false);
  readonly errorMessage = signal('');
  readonly success = signal(false);
  readonly profileImage = signal<File | null>(null);
  readonly profileImagePreview = signal<string | null>(null);
  readonly profileImageError = signal('');
  readonly serverErrors = signal<Partial<Record<RegistrationField, string>>>({});

  readonly form = this.formBuilder.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.required, Validators.maxLength(30), Validators.pattern(/^(?=.*\d)\+?[\d\s().-]+$/)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
    confirmPassword: ['', [Validators.required, Validators.maxLength(128)]]
  }, { validators: passwordsMatch });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.serverErrors.set({});
      this.errorMessage.set('');
    });
  }

  submit(): void {
    if (this.submitting() || this.success()) return;
    this.errorMessage.set('');
    this.serverErrors.set({});
    for (const field of ['firstName', 'lastName', 'email', 'phoneNumber'] as const) {
      this.form.controls[field].setValue(this.form.controls[field].value.trim());
    }
    this.form.markAllAsTouched();
    if (this.form.invalid || this.profileImageError()) return;

    this.submitting.set(true);
    const values = this.form.getRawValue();
    this.auth.register({
      firstName: values.firstName,
      lastName: values.lastName,
      email: values.email,
      phoneNumber: values.phoneNumber,
      password: values.password,
      profileImage: this.profileImage()
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.submitting.set(false))
    ).subscribe({
      next: () => {
        this.form.reset();
        this.passwordVisible.set(false);
        this.success.set(true);
        this.removeProfileImage();
        this.errorMessage.set('');
      },
      error: (error: unknown) => this.handleError(error)
    });
  }

  onProfileImageChange(event: Event): void {
    if (this.submitting()) return;
    const input = event.target as HTMLInputElement;
    this.fileInput = input;
    this.clearPreview();
    this.profileImageError.set('');
    const file = input.files?.item(0) ?? null;
    if (!file) return;

    const extension = file.name.toLowerCase().match(/\.(jpe?g|png)$/)?.[1];
    const allowedType = !file.type || (extension === 'png'
      ? file.type === 'image/png'
      : file.type === 'image/jpeg' || file.type === 'image/jpg');
    if (!extension || !allowedType) {
      this.profileImageError.set('Profil resmi JPG veya PNG biçiminde olmalıdır.');
    } else if (file.size === 0) {
      this.profileImageError.set('Boş bir profil resmi yüklenemez.');
    } else if (file.size > 2 * 1024 * 1024) {
      this.profileImageError.set('Profil resmi en fazla 2 MB olabilir.');
    }

    if (this.profileImageError()) {
      input.value = '';
      return;
    }

    try {
      this.profileImagePreview.set(URL.createObjectURL(file));
      this.profileImage.set(file);
    } catch {
      this.profileImageError.set('Profil resmi önizlenemedi. Başka bir resim seçin.');
      input.value = '';
    }
  }

  removeProfileImage(input?: HTMLInputElement): void {
    if (this.submitting() && !this.success()) return;
    this.clearPreview();
    this.profileImageError.set('');
    const fileInput = input ?? this.fileInput;
    if (fileInput) fileInput.value = '';
    this.fileInput = null;
  }

  onImagePreviewError(event: Event, input?: HTMLInputElement): void {
    const preview = this.profileImagePreview();
    const image = event.target as HTMLImageElement | null;
    if (!preview || !image || image.getAttribute('src') !== preview
      || (image.currentSrc && image.currentSrc !== preview)) return;
    this.clearPreview();
    this.profileImageError.set('Seçilen dosya görüntülenemiyor. Geçerli bir JPG veya PNG resmi seçin.');
    const fileInput = input ?? this.fileInput;
    if (fileInput) fileInput.value = '';
  }

  fieldError(field: FormField): string | null {
    const serverError = this.serverErrors()[field];
    if (serverError) return serverError;
    const control = this.form.controls[field];
    if (!control.touched) return null;
    if (control.hasError('required')) {
      return {
        firstName: 'Adınızı girin.',
        lastName: 'Soyadınızı girin.',
        email: 'E-posta adresinizi girin.',
        phoneNumber: 'Telefon numaranızı girin.',
        password: 'Parolanızı girin.',
        confirmPassword: 'Parolanızı tekrar girin.'
      }[field];
    }
    if (control.hasError('maxlength')) {
      const limit = field === 'email' ? 256 : field === 'phoneNumber' ? 30
        : field === 'password' || field === 'confirmPassword' ? 128 : 100;
      return `Bu alan en fazla ${limit} karakter olabilir.`;
    }
    if (control.hasError('email')) return 'Geçerli bir e-posta adresi girin.';
    if (control.hasError('pattern')) return 'Geçerli bir telefon numarası girin.';
    if (control.hasError('minlength')) return 'Parolanız en az 12 karakter olmalıdır.';
    if (field === 'confirmPassword' && this.form.hasError('passwordMismatch')) {
      return 'Parolalar aynı olmalıdır.';
    }
    return null;
  }

  ngOnDestroy(): void {
    this.clearPreview();
  }

  private clearPreview(): void {
    const preview = this.profileImagePreview();
    if (preview) URL.revokeObjectURL(preview);
    this.profileImagePreview.set(null);
    this.profileImage.set(null);
  }

  private handleError(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      this.errorMessage.set('Kayıt oluşturulamadı. Lütfen tekrar deneyin.');
      return;
    }
    if (error.status === 409) {
      this.serverErrors.set({ email: 'Bu e-posta adresi zaten kayıtlı.' });
      this.errorMessage.set('Bu e-posta adresi zaten kayıtlı. Giriş yapabilir veya farklı bir adres kullanabilirsiniz.');
      return;
    }
    if (error.status === 400) {
      const fieldMessages: Partial<Record<RegistrationField, string>> = {};
      const messages: Record<string, [RegistrationField, string]> = {
        firstname: ['firstName', 'Adınızı kontrol edin. En fazla 100 karakter kullanabilirsiniz.'],
        lastname: ['lastName', 'Soyadınızı kontrol edin. En fazla 100 karakter kullanabilirsiniz.'],
        email: ['email', 'Geçerli bir e-posta adresi girin. En fazla 256 karakter kullanabilirsiniz.'],
        phonenumber: ['phoneNumber', 'Geçerli bir telefon numarası girin. En fazla 30 karakter kullanabilirsiniz.'],
        password: ['password', 'Parolanız 12 ile 128 karakter arasında olmalıdır.'],
        profileimage: ['profileImage', 'Profil resmi JPG veya PNG biçiminde ve en fazla 2 MB olmalıdır.']
      };
      const body = error.error;
      if (typeof body === 'object' && body !== null && !Array.isArray(body)
        && typeof body.errors === 'object' && body.errors !== null && !Array.isArray(body.errors)) {
        for (const key of Object.keys(body.errors)) {
          const fieldName = key.split('.').pop()!.toLowerCase();
          const field = Object.hasOwn(messages, fieldName) ? messages[fieldName] : undefined;
          if (field) fieldMessages[field[0]] = field[1];
        }
      }
      this.serverErrors.set(fieldMessages);
      if (fieldMessages.profileImage) this.profileImageError.set(fieldMessages.profileImage);
      this.errorMessage.set('Bilgilerinizi kontrol edip tekrar deneyin.');
      return;
    }
    if (error.status === 413) {
      this.profileImageError.set('Dosya çok büyük. En fazla 2 MB bir profil resmi seçin.');
      this.errorMessage.set('Yüklenen dosya çok büyük.');
      return;
    }
    if ([0, 502, 503, 504].includes(error.status)) {
      this.errorMessage.set('Şu anda kayıt hizmetine ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.');
      return;
    }
    this.errorMessage.set('Kayıt oluşturulamadı. Lütfen tekrar deneyin.');
  }
}

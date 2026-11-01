import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly submitting = signal(false);
  readonly passwordVisible = signal(false);
  readonly errorMessage = signal('');

  readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  submit(): void {
    if (this.submitting()) return;

    this.errorMessage.set('');
    this.form.controls.email.setValue(this.form.controls.email.value.trim());
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    this.auth.login(this.form.getRawValue())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => {
          this.form.controls.password.reset();
          void this.router.navigate(['/meetings'], { replaceUrl: true });
        },
        error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
      });
  }

  private describeError(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Giriş yapılamadı. Lütfen tekrar deneyin.';
    }

    if (error.status === 401) return 'E-posta veya parola hatalı.';
    if (error.status === 400) return 'E-posta ve parola bilgilerinizi kontrol edin.';
    if ([0, 502, 503, 504].includes(error.status)) {
      return 'Şu anda giriş hizmetine ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.';
    }

    return 'Giriş yapılamadı. Lütfen tekrar deneyin.';
  }
}

import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthService, UserProfile } from '../../core/auth/auth.service';
import { getBrowserTimeZone } from '../../core/auth/browser-time-zone';

@Component({
  selector: 'app-account',
  imports: [ReactiveFormsModule],
  templateUrl: './account.html',
  styleUrl: './account.css'
})
export class Account implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly profile = signal<UserProfile | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly savingTimeZone = signal(false);
  protected readonly timeZoneError = signal('');
  protected readonly timeZoneSaved = signal(false);
  protected readonly timeZoneForm = new FormGroup({
    timeZoneId: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] })
  });

  ngOnInit(): void {
    this.loadProfile();
  }

  protected loadProfile(): void {
    if (this.loading() || this.savingTimeZone()) return;
    this.loading.set(true);
    this.error.set('');
    this.auth.loadProfile().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: profile => {
        this.profile.set(profile);
        this.timeZoneForm.reset({ timeZoneId: profile.timeZoneId ?? '' });
        this.timeZoneError.set('');
        this.timeZoneSaved.set(false);
      },
      error: (error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
          this.error.set('Hesap bilgileri alınamadı. Lütfen yeniden deneyin.');
        }
      }
    });
  }

  protected useBrowserTimeZone(): void {
    if (this.savingTimeZone()) return;
    const timeZoneId = getBrowserTimeZone();
    this.timeZoneSaved.set(false);
    if (!timeZoneId) {
      this.timeZoneError.set('Tarayıcınızın saat dilimi belirlenemedi. Saat dilimini elle girebilirsiniz.');
      return;
    }
    this.timeZoneForm.controls.timeZoneId.setValue(timeZoneId);
    this.timeZoneForm.markAsDirty();
    this.timeZoneError.set('');
  }

  protected clearTimeZoneFeedback(): void {
    this.timeZoneSaved.set(false);
    this.timeZoneError.set('');
  }

  protected saveTimeZone(): void {
    if (this.loading() || this.savingTimeZone() || !this.profile()) return;
    this.timeZoneSaved.set(false);
    this.timeZoneError.set('');
    const timeZoneId = this.timeZoneForm.controls.timeZoneId.value.trim();
    this.timeZoneForm.controls.timeZoneId.setValue(timeZoneId);
    this.timeZoneForm.markAllAsTouched();
    if (this.timeZoneForm.invalid) {
      this.timeZoneError.set('Saat dilimi en fazla 100 karakter olabilir.');
      return;
    }
    this.savingTimeZone.set(true);
    this.auth.updateTimeZone(timeZoneId || null).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.savingTimeZone.set(false))
    ).subscribe({
      next: profile => {
        this.profile.set(profile);
        this.timeZoneForm.reset({ timeZoneId: profile.timeZoneId ?? '' });
        this.timeZoneSaved.set(true);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) return;
        this.timeZoneError.set(error instanceof HttpErrorResponse && error.status === 400
          ? 'Geçerli bir IANA saat dilimi girin. Örnek: Europe/Istanbul veya Europe/Rome.'
          : 'Saat dilimi kaydedilemedi. Lütfen yeniden deneyin.');
      }
    });
  }
}

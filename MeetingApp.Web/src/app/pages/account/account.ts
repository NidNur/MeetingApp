import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { AuthService, UserProfile } from '../../core/auth/auth.service';

@Component({
  selector: 'app-account',
  templateUrl: './account.html',
  styleUrl: './account.css'
})
export class Account implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly profile = signal<UserProfile | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void {
    this.loadProfile();
  }

  protected loadProfile(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.auth.loadProfile().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: profile => {
        this.profile.set(profile);
      },
      error: (error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
          this.error.set('Hesap bilgileri alınamadı. Lütfen yeniden deneyin.');
        }
      }
    });
  }
}

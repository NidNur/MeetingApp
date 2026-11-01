import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription, distinctUntilChanged, finalize, map } from 'rxjs';
import { getBrowserTimeZone } from '../../core/auth/browser-time-zone';
import { toLocalDateTimeInput, toUtcIso } from '../../core/meetings/meeting-dates';
import { Meeting, MeetingInput } from '../../core/meetings/meeting.models';
import { MeetingsService } from '../../core/meetings/meetings.service';

type FormField = 'name' | 'description' | 'startsAt' | 'endsAt';
type DateField = 'startsAt' | 'endsAt';
type ServerField = FormField | 'document';

const validLocalDate: ValidatorFn = control => !control.value || toUtcIso(control.value)
  ? null : { localDateTime: true };

@Component({
  selector: 'app-meeting-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './meeting-form.html',
  styleUrl: './meeting-form.css'
})
export class MeetingForm {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly meetings = inject(MeetingsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private loadSubscription?: Subscription;
  private saveSubscription?: Subscription;
  private routeVersion = 0;
  private routeId: string | null = null;
  private fileInput: HTMLInputElement | null = null;
  private originalLocalTimes: Record<DateField, string> | null = null;

  readonly editing = signal(false);
  readonly meeting = signal<Meeting | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly unavailable = signal(false);
  readonly canRetry = signal(false);
  readonly conflict = signal(false);
  readonly completed = signal(false);
  readonly errorMessage = signal('');
  readonly serverErrors = signal<Partial<Record<ServerField, string>>>({});
  readonly document = signal<File | null>(null);
  readonly documentError = signal('');
  readonly timeZone = getBrowserTimeZone() ?? 'tarayıcınızın yerel saat dilimi';
  readonly heading = computed(() => this.editing() ? 'Toplantıyı düzenle' : 'Yeni toplantı');
  readonly readOnly = computed(() => this.loading() || this.saving() || this.unavailable()
    || this.conflict() || this.completed() || !!this.meeting()?.isCancelled);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.required, Validators.maxLength(4000)]],
    startsAt: ['', [Validators.required, validLocalDate]],
    endsAt: ['', [Validators.required, validLocalDate]]
  }, { validators: (control: AbstractControl): ValidationErrors | null => {
    const start = this.resolveUtc('startsAt', control.get('startsAt')?.value ?? '');
    const end = this.resolveUtc('endsAt', control.get('endsAt')?.value ?? '');
    return start && end && Date.parse(end) <= Date.parse(start) ? { endBeforeStart: true } : null;
  } });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.serverErrors.set({});
      this.errorMessage.set('');
    });
    this.route.paramMap.pipe(
      map(params => params.get('id')),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(id => this.loadRoute(id));
  }

  retry(): void {
    if (!this.loading() && this.canRetry()) this.loadRoute(this.routeId);
  }

  submit(): void {
    if (this.readOnly()) return;
    this.errorMessage.set('');
    this.serverErrors.set({});
    for (const field of ['name', 'description'] as const) {
      this.form.controls[field].setValue(this.form.controls[field].value.trim());
    }
    this.form.markAllAsTouched();
    if (this.form.invalid || this.documentError()) return;

    const values = this.form.getRawValue();
    const startsAtUtc = this.resolveUtc('startsAt', values.startsAt);
    const endsAtUtc = this.resolveUtc('endsAt', values.endsAt);
    if (!startsAtUtc || !endsAtUtc) return;
    const input: MeetingInput = {
      name: values.name,
      description: values.description,
      startsAtUtc,
      endsAtUtc,
      document: this.document()
    };
    const current = this.meeting();
    if (this.editing() && !current) return;
    const version = this.routeVersion;
    this.saving.set(true);
    const request = current ? this.meetings.update(current.id, input) : this.meetings.create(input);
    this.saveSubscription = request.pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => { if (version === this.routeVersion) this.saving.set(false); })
    ).subscribe({
      next: () => {
        if (version !== this.routeVersion) return;
        this.completed.set(true);
        void this.router.navigate(['/meetings'], {
          state: { meetingMessage: current ? 'Toplantı güncellendi.' : 'Toplantı oluşturuldu.' }
        });
      },
      error: (error: unknown) => {
        if (version === this.routeVersion) this.handleError(error, false);
      }
    });
  }

  onDocumentChange(event: Event): void {
    if (this.readOnly()) return;
    const input = event.target as HTMLInputElement;
    this.fileInput = input;
    this.document.set(null);
    this.documentError.set('');
    this.serverErrors.update(errors => ({ ...errors, document: undefined }));
    const file = input.files?.item(0) ?? null;
    if (!file) return;

    const extension = file.name.toLowerCase().match(/\.(pdf|docx)$/)?.[1];
    const mimeTypes = extension === 'pdf'
      ? ['', 'application/pdf', 'application/octet-stream']
      : ['', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'application/zip', 'application/octet-stream'];
    if (!extension || !mimeTypes.includes(file.type.toLowerCase())) {
      this.documentError.set('Doküman PDF veya DOCX biçiminde olmalıdır.');
    } else if (file.size === 0) {
      this.documentError.set('Boş bir doküman yüklenemez.');
    } else if (file.size > 10 * 1024 * 1024) {
      this.documentError.set('Doküman en fazla 10 MB olabilir.');
    }
    if (this.documentError()) {
      input.value = '';
      return;
    }
    this.document.set(file);
  }

  removeDocument(input?: HTMLInputElement): void {
    if (this.readOnly()) return;
    this.clearDocument(input);
    this.serverErrors.update(errors => ({ ...errors, document: undefined }));
  }

  fieldError(field: FormField): string | null {
    const serverError = this.serverErrors()[field];
    if (serverError) return serverError;
    const control = this.form.controls[field];
    if (!control.touched) return null;
    if (control.hasError('required')) {
      return {
        name: 'Toplantı adını girin.',
        description: 'Toplantı açıklamasını girin.',
        startsAt: 'Başlangıç tarihini ve saatini girin.',
        endsAt: 'Bitiş tarihini ve saatini girin.'
      }[field];
    }
    if (control.hasError('maxlength')) {
      return `Bu alan en fazla ${field === 'name' ? 200 : 4000} karakter olabilir.`;
    }
    if (control.hasError('localDateTime')) return 'Geçerli bir yerel tarih ve saat girin.';
    if (field === 'endsAt' && this.form.hasError('endBeforeStart')) {
      return 'Bitiş tarihi başlangıç tarihinden sonra olmalıdır.';
    }
    return null;
  }

  private loadRoute(id: string | null): void {
    const version = ++this.routeVersion;
    this.loadSubscription?.unsubscribe();
    this.saveSubscription?.unsubscribe();
    this.routeId = id;
    this.loading.set(false);
    this.saving.set(false);
    this.editing.set(id !== null);
    this.meeting.set(null);
    this.originalLocalTimes = null;
    this.unavailable.set(false);
    this.canRetry.set(false);
    this.conflict.set(false);
    this.completed.set(false);
    this.clearDocument();
    this.form.reset({ name: '', description: '', startsAt: '', endsAt: '' });
    this.errorMessage.set('');
    this.serverErrors.set({});
    if (id === null) return;
    const meetingId = Number(id);
    if (!/^[1-9]\d*$/.test(id) || !Number.isSafeInteger(meetingId) || meetingId > 2147483647) {
      this.unavailable.set(true);
      this.errorMessage.set('Geçersiz toplantı bağlantısı. Toplantı listesinden tekrar deneyin.');
      return;
    }

    this.loading.set(true);
    this.loadSubscription = this.meetings.getById(meetingId).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => { if (version === this.routeVersion) this.loading.set(false); })
    ).subscribe({
      next: meeting => {
        if (version !== this.routeVersion) return;
        this.meeting.set(meeting);
        this.originalLocalTimes = {
          startsAt: toLocalDateTimeInput(meeting.startsAtUtc),
          endsAt: toLocalDateTimeInput(meeting.endsAtUtc)
        };
        this.form.reset({ name: meeting.name, description: meeting.description, ...this.originalLocalTimes });
      },
      error: (error: unknown) => {
        if (version === this.routeVersion) this.handleError(error, true);
      }
    });
  }

  private resolveUtc(field: DateField, value: string): string | null {
    const original = this.meeting();
    if (original && this.originalLocalTimes?.[field] === value) {
      return field === 'startsAt' ? original.startsAtUtc : original.endsAtUtc;
    }
    return toUtcIso(value);
  }

  private clearDocument(input?: HTMLInputElement): void {
    const target = input ?? this.fileInput;
    if (target) target.value = '';
    this.fileInput = null;
    this.document.set(null);
    this.documentError.set('');
  }

  private handleError(error: unknown, loading: boolean): void {
    if (loading) {
      this.unavailable.set(true);
      this.canRetry.set(true);
    }
    if (!(error instanceof HttpErrorResponse)) {
      this.errorMessage.set(loading ? 'Toplantı yüklenemedi. Lütfen tekrar deneyin.' : 'Toplantı kaydedilemedi. Lütfen tekrar deneyin.');
      return;
    }
    if (error.status === 404) {
      this.unavailable.set(true);
      this.canRetry.set(false);
      this.errorMessage.set('Toplantı bulunamadı veya bu toplantıya erişiminiz yok.');
      return;
    }
    if (error.status === 409) {
      this.conflict.set(true);
      this.canRetry.set(true);
      this.errorMessage.set(this.editing()
        ? 'Toplantı başka bir işlem nedeniyle değişmiş olabilir. Devam etmek için bilgileri yeniden yükleyin.'
        : 'Toplantı kaydedilemedi. Devam etmek için formu yenileyin.');
      return;
    }
    if (error.status === 400 && !loading) {
      const messages: Record<string, [ServerField, string]> = {
        name: ['name', 'Toplantı adını kontrol edin. En fazla 200 karakter kullanabilirsiniz.'],
        description: ['description', 'Açıklamayı kontrol edin. En fazla 4000 karakter kullanabilirsiniz.'],
        startsatutc: ['startsAt', 'Geçerli bir başlangıç tarihi ve saati girin.'],
        endsatutc: ['endsAt', 'Bitiş tarihi başlangıç tarihinden sonra olmalıdır.'],
        document: ['document', 'Geçerli, boş olmayan bir PDF veya DOCX dokümanı seçin. En fazla 10 MB yükleyebilirsiniz.']
      };
      const errors: Partial<Record<ServerField, string>> = {};
      const body: unknown = error.error;
      if (typeof body === 'object' && body !== null && !Array.isArray(body) && 'errors' in body) {
        const fields = body.errors;
        if (typeof fields === 'object' && fields !== null && !Array.isArray(fields)) {
          for (const key of Object.keys(fields)) {
            const fieldName = key.split('.').pop()!.toLowerCase();
            const field = Object.hasOwn(messages, fieldName) ? messages[fieldName] : undefined;
            if (field) errors[field[0]] = field[1];
          }
        }
      }
      this.serverErrors.set(errors);
      if (errors.document) this.documentError.set(errors.document);
      this.errorMessage.set('Bilgilerinizi kontrol edip tekrar deneyin.');
      return;
    }
    if (error.status === 413 && !loading) {
      this.documentError.set('Dosya çok büyük. En fazla 10 MB bir doküman seçin.');
      this.errorMessage.set('Yüklenen dosya çok büyük.');
      return;
    }
    if (error.status === 401) {
      this.errorMessage.set('Oturumunuz sona erdi. Devam etmek için tekrar giriş yapın.');
      return;
    }
    if ([0, 502, 503, 504].includes(error.status)) {
      this.errorMessage.set('Şu anda toplantı hizmetine ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.');
      return;
    }
    this.errorMessage.set(loading ? 'Toplantı yüklenemedi. Lütfen tekrar deneyin.' : 'Toplantı kaydedilemedi. Lütfen tekrar deneyin.');
  }
}

import { DOCUMENT, DatePipe, Location } from '@angular/common';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize, Observable, switchMap, of } from 'rxjs';
import { getBrowserTimeZone } from '../../core/auth/browser-time-zone';
import { Meeting } from '../../core/meetings/meeting.models';
import { MeetingsService } from '../../core/meetings/meetings.service';

type MeetingAction = 'cancel' | 'delete' | 'deleteDocument';
interface Confirmation { action: MeetingAction; meeting: Meeting; }

@Component({
  selector: 'app-meetings',
  standalone: true,
  imports: [DatePipe, RouterLink],
  templateUrl: './meetings.html',
  styleUrl: './meetings.css'
})
export class Meetings implements OnInit {
  private readonly service = inject(MeetingsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly location = inject(Location);
  @ViewChild('confirmationDialog', { static: true }) private dialog!: ElementRef<HTMLDialogElement>;
  private readonly objectUrls = new Map<string, ReturnType<typeof setTimeout>>();
  private lastRequestedPage = 1;

  protected readonly meetings = signal<Meeting[]>([]);
  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = 20;
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));
  protected readonly firstItem = computed(() => (this.page() - 1) * this.pageSize + 1);
  protected readonly lastItem = computed(() => Math.min(this.page() * this.pageSize, this.totalCount()));
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly feedback = signal('');
  protected readonly documentError = signal('');
  protected readonly downloadingId = signal<number | null>(null);
  protected readonly confirmation = signal<Confirmation | null>(null);
  protected readonly submitting = signal(false);
  protected readonly confirmationError = signal('');
  protected readonly needsRefresh = signal(false);
  protected readonly browserTimeZone = getBrowserTimeZone() ?? 'tarayıcınızın yerel saat dilimi';
  protected readonly confirmationTitle = computed(() => {
    switch (this.confirmation()?.action) {
      case 'cancel': return 'Toplantı iptal edilsin mi?';
      case 'delete': return 'Toplantı silinsin mi?';
      case 'deleteDocument': return 'Belge silinsin mi?';
      default: return '';
    }
  });
  protected readonly confirmLabel = computed(() => {
    switch (this.confirmation()?.action) {
      case 'cancel': return 'Toplantıyı iptal et';
      case 'delete': return 'Toplantıyı sil';
      case 'deleteDocument': return 'Belgeyi sil';
      default: return 'Onayla';
    }
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      for (const [url, timer] of this.objectUrls) {
        clearTimeout(timer);
        URL.revokeObjectURL(url);
      }
      this.objectUrls.clear();
    });
  }

  ngOnInit(): void {
    const state: unknown = this.location.getState();
    if (typeof state === 'object' && state !== null && 'meetingMessage' in state
      && (state.meetingMessage === 'Toplantı oluşturuldu.' || state.meetingMessage === 'Toplantı güncellendi.')) {
      this.feedback.set(state.meetingMessage);
    }
    this.loadPage();
  }

  protected loadPage(requestedPage = this.lastRequestedPage): void {
    if (this.loading() || this.submitting() || this.confirmation()) return;
    if (!Number.isInteger(requestedPage) || requestedPage < 1) return;
    this.lastRequestedPage = requestedPage;
    this.loading.set(true);
    this.error.set('');
    this.documentError.set('');
    this.service.list(requestedPage, this.pageSize).pipe(
      // A page can disappear when another browser deletes its final meeting.
      switchMap(response => {
        const lastPage = Math.max(1, Math.ceil(response.totalCount / this.pageSize));
        return requestedPage > lastPage
          ? this.service.list(lastPage, this.pageSize)
          : of(response);
      }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: response => {
        this.meetings.set(response.items);
        this.totalCount.set(response.totalCount);
        this.page.set(response.page);
        this.lastRequestedPage = response.page;
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) return;
        this.error.set('Toplantılar yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.');
      }
    });
  }

  protected openConfirmation(action: MeetingAction, meeting: Meeting): void {
    if (this.loading() || this.submitting() || this.confirmation()) return;
    if (action === 'cancel' && meeting.isCancelled) return;
    if (action === 'deleteDocument' && (meeting.isCancelled || !meeting.hasDocument)) return;
    this.confirmationError.set('');
    this.needsRefresh.set(false);
    this.confirmation.set({ action, meeting });
    this.dialog.nativeElement.showModal();
  }

  protected dismissConfirmation(): void {
    if (this.submitting()) return;
    const refresh = this.needsRefresh();
    this.closeDialog();
    if (refresh) this.loadPage();
  }

  protected onDialogCancel(event: Event): void {
    event.preventDefault();
    this.dismissConfirmation();
  }

  protected onDialogClose(): void {
    // Also recover if the browser closes the dialog through a native action.
    if (!this.dialog.nativeElement.open && !this.submitting() && this.confirmation()) this.dismissConfirmation();
  }

  protected confirmAction(): void {
    const pending = this.confirmation();
    if (!pending || this.submitting() || this.needsRefresh()) return;
    this.submitting.set(true);
    this.confirmationError.set('');
    this.feedback.set('');
    let request: Observable<Meeting | void>;
    switch (pending.action) {
      case 'cancel': request = this.service.cancel(pending.meeting.id); break;
      case 'delete': request = this.service.delete(pending.meeting.id); break;
      case 'deleteDocument': request = this.service.deleteDocument(pending.meeting.id); break;
    }
    request.pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.submitting.set(false))
    ).subscribe({
      next: () => {
        const nextPage = pending.action === 'delete' && this.meetings().length === 1
          ? Math.max(1, this.page() - 1) : this.page();
        this.feedback.set(pending.action === 'cancel' ? 'Toplantı iptal edildi.'
          : pending.action === 'delete' ? 'Toplantı silindi.' : 'Belge silindi.');
        this.submitting.set(false);
        this.closeDialog();
        this.loadPage(nextPage);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.closeDialog();
          return;
        }
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        this.needsRefresh.set(status === 404 || status === 409);
        this.confirmationError.set(status === 404
          ? 'Toplantı veya belge artık bulunamıyor. Güncel durumu görmek için listeyi yenileyin.'
          : status === 409
            ? 'Toplantının durumu değişmiş veya toplantı iptal edilmiş olabilir. Listeyi yenileyin.'
            : 'İşlem tamamlanamadı. Bağlantınızı kontrol edip yeniden deneyin.');
      }
    });
  }

  protected downloadDocument(meeting: Meeting): void {
    if (!meeting.hasDocument || this.downloadingId() !== null || this.loading() || this.submitting()) return;
    this.downloadingId.set(meeting.id);
    this.documentError.set('');
    this.service.downloadDocument(meeting.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.downloadingId.set(null))
    ).subscribe({
      next: response => this.saveDocument(response, meeting),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) return;
        this.documentError.set(error instanceof HttpErrorResponse && error.status === 404
          ? 'Belge artık bulunamıyor. Güncel durumu görmek için listeyi yenileyin.'
          : 'Belge indirilemedi. Bağlantınızı kontrol edip yeniden deneyin.');
      }
    });
  }

  private closeDialog(): void {
    this.confirmation.set(null);
    this.confirmationError.set('');
    this.needsRefresh.set(false);
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  private saveDocument(response: HttpResponse<Blob>, meeting: Meeting): void {
    const blob = response.body;
    if (!blob || blob.size === 0) {
      this.documentError.set('Belge boş olduğu için indirilemedi.');
      return;
    }
    let anchor: HTMLAnchorElement | undefined;
    let url: string | undefined;
    try {
      url = URL.createObjectURL(blob);
      anchor = this.document.createElement('a');
      anchor.href = url;
      anchor.download = this.documentName(response.headers.get('Content-Disposition'), meeting);
      anchor.hidden = true;
      this.document.body.appendChild(anchor);
      anchor.click();
    } catch {
      this.documentError.set('Belge indirilemedi. Lütfen yeniden deneyin.');
    } finally {
      anchor?.remove();
      if (url) {
        const objectUrl = url;
        const timer = setTimeout(() => {
          URL.revokeObjectURL(objectUrl);
          this.objectUrls.delete(objectUrl);
        }, 0);
        this.objectUrls.set(objectUrl, timer);
      }
    }
  }

  private documentName(disposition: string | null, meeting: Meeting): string {
    let headerName: string | undefined;
    const encoded = disposition?.match(/filename\*\s*=\s*UTF-8''([^;]+)/i)?.[1];
    if (encoded) {
      try { headerName = decodeURIComponent(encoded.trim().replace(/^"|"$/g, '')); } catch { /* Use the original name. */ }
    }
    if (!headerName) {
      const plain = disposition?.match(/filename\s*=\s*(?:"([^"]*)"|([^;]*))/i);
      headerName = plain?.[1] ?? plain?.[2]?.trim();
    }
    const fallback = `toplanti-${meeting.id}-belge`;
    const safe = (name: string | null | undefined): string => (name ?? '').split(/[\\/]/).pop()!
      .replace(/[\u0000-\u001f\u007f\u202a-\u202e\u2066-\u2069]/g, '')
      .replace(/[<>:"|?*]/g, '_').replace(/^\.+|[.\s]+$/g, '').trim().slice(0, 200);
    return safe(headerName) || safe(meeting.documentOriginalName) || fallback;
  }
}

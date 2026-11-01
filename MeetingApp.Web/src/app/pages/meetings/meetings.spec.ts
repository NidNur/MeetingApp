import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Meeting } from '../../core/meetings/meeting.models';
import { Meetings } from './meetings';

describe('Meetings list', () => {
  let fixture: ComponentFixture<Meetings>;
  let backend: HttpTestingController;
  let objectUrl: jasmine.Spy;
  let revokeUrl: jasmine.Spy;
  let anchorClick: jasmine.Spy;
  const active: Meeting = {
    id: 7, name: 'Tasarım toplantısı', description: 'Yeni ekranları görüşelim.',
    startsAtUtc: '2030-01-12T09:15:00Z', endsAtUtc: '2030-01-12T10:30:00Z',
    createdAtUtc: '2029-12-01T12:00:00Z', isCancelled: false, cancelledAtUtc: null,
    documentOriginalName: 'gündem.pdf', hasDocument: true
  };
  const cancelled: Meeting = {
    ...active, id: 8, name: 'İptal edilen toplantı', isCancelled: true,
    cancelledAtUtc: '2029-12-02T12:00:00Z'
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Meetings],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    objectUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:meeting-document');
    revokeUrl = spyOn(URL, 'revokeObjectURL');
    // No test triggers a real download or writes a file.
    anchorClick = spyOn(HTMLAnchorElement.prototype, 'click');
    fixture = TestBed.createComponent(Meetings);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    backend.verify();
  });

  const page = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const dialog = (): HTMLDialogElement => page().querySelector('dialog')!;
  const row = (id = 7): HTMLTableRowElement => page().querySelector(`[data-meeting-id="${id}"]`)!;
  const action = (name: string, id = 7): HTMLButtonElement => row(id).querySelector(`[data-action="${name}"]`)!;
  const submit = (): HTMLButtonElement => page().querySelector('[data-confirm="submit"]')!;
  const dismiss = (): HTMLButtonElement => page().querySelector('[data-confirm="dismiss"]')!;
  function listRequest(number = 1): TestRequest {
    const request = backend.expectOne(req => req.url === '/api/meetings' && req.params.get('page') === String(number));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('pageSize')).toBe('20');
    return request;
  }
  function flushList(items: Meeting[] = [active, cancelled], totalCount = items.length, number = 1): void {
    listRequest(number).flush({ items, totalCount, page: number, pageSize: 20 });
    fixture.detectChanges();
  }
  function open(name: string, id = 7): void {
    action(name, id).click();
    fixture.detectChanges();
    expect(dialog().open).toBeTrue();
  }
  function confirm(): void {
    submit().click();
    fixture.detectChanges();
  }
  function localText(iso: string): string {
    const value = new Date(iso);
    const pad = (number: number): string => String(number).padStart(2, '0');
    return `${pad(value.getDate())}.${pad(value.getMonth() + 1)}.${value.getFullYear()} ${pad(value.getHours())}:${pad(value.getMinutes())}`;
  }

  it('loads a server page of twenty and gives an empty state with a creation link', () => {
    expect(page().textContent).toContain('Toplantılar yükleniyor');
    expect(page().querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    flushList([]);
    expect(page().textContent).toContain('Henüz toplantınız yok');
    expect(page().querySelector('.empty a')?.getAttribute('href')).toBe('/meetings/new');
    expect(page().querySelector('table')).toBeNull();
    expect(page().querySelector('section')?.getAttribute('aria-busy')).toBe('false');
  });

  it('renders local times, browser zone meaning, status and edit/new navigation', () => {
    flushList();
    expect(page().querySelector('.time-zone')?.textContent).toContain('tarayıcınızın saat diliminde');
    expect(row().querySelector('time')?.textContent).toBe(localText(active.startsAtUtc));
    expect(row().querySelector('time')?.getAttribute('datetime')).toBe(active.startsAtUtc);
    expect(row().querySelector('.status')?.textContent).toBe('Etkin');
    expect(row(8).querySelector('.status')?.textContent).toBe('İptal edildi');
    expect(row().querySelector('[data-action="edit"]')?.getAttribute('href')).toBe('/meetings/7/edit');
    expect(page().querySelector('.page-heading a')?.getAttribute('href')).toBe('/meetings/new');
  });

  it('shows the creation result when returning from the form', () => {
    flushList([]);
    fixture.destroy();
    spyOn(TestBed.inject(Location), 'getState').and.returnValue({ meetingMessage: 'Toplantı oluşturuldu.' });
    fixture = TestBed.createComponent(Meetings);
    fixture.detectChanges();
    flushList([active]);
    expect(page().querySelector('[role="status"]')?.textContent).toContain('Toplantı oluşturuldu.');
  });

  it('keeps cancelled meetings downloadable and deletable while removing edit, cancel and document deletion', () => {
    flushList();
    expect(action('download', 8)).not.toBeNull();
    expect(action('delete', 8)).not.toBeNull();
    expect(action('edit', 8)).toBeNull();
    expect(action('cancel', 8)).toBeNull();
    expect(action('deleteDocument', 8)).toBeNull();
  });

  it('shows a friendly load error and retries without exposing server details', () => {
    listRequest().flush({ detail: 'private SQL stack trace' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Toplantılar yüklenemedi');
    expect(page().textContent).not.toContain('private SQL');
    (page().querySelector('.state-panel button') as HTMLButtonElement).click();
    fixture.detectChanges();
    flushList();
    expect(page().querySelector('[role="alert"]')).toBeNull();
    expect(row().textContent).toContain(active.name);
  });

  it('paginates on the server and blocks duplicate page requests', () => {
    flushList([active], 41);
    expect((page().querySelector('[data-page="previous"]') as HTMLButtonElement).disabled).toBeTrue();
    const next = page().querySelector('[data-page="next"]') as HTMLButtonElement;
    next.click();
    next.dispatchEvent(new Event('click'));
    fixture.detectChanges();
    flushList([{ ...active, id: 21 }], 41, 2);
    expect(page().querySelector('.pagination')?.textContent).toContain('21–40');
    expect(page().querySelector('.pagination')?.textContent).toContain('Sayfa 2 / 3');
    (page().querySelector('[data-page="previous"]') as HTMLButtonElement).click();
    flushList([active], 41, 1);
  });

  it('retries the requested page after a later page fails', () => {
    flushList([active], 21);
    (page().querySelector('[data-page="next"]') as HTMLButtonElement).click();
    listRequest(2).error(new ProgressEvent('error'));
    fixture.detectChanges();
    (page().querySelector('.state-panel button') as HTMLButtonElement).click();
    flushList([{ ...active, id: 21 }], 21, 2);
    expect(page().querySelector('.pagination')?.textContent).toContain('Sayfa 2 / 2');
  });

  it('recovers when a requested page has disappeared after a concurrent deletion', () => {
    flushList([active], 21);
    (page().querySelector('[data-page="next"]') as HTMLButtonElement).click();
    listRequest(2).flush({ items: [], totalCount: 20, page: 2, pageSize: 20 });
    flushList([active], 20, 1);
    expect(page().querySelector('.pagination')?.textContent).toContain('Sayfa 1 / 1');
  });

  it('requires a native confirmation and leaves the meeting intact when dismissed', () => {
    flushList();
    open('cancel');
    expect(dialog().textContent).toContain('Toplantı iptal edilsin mi?');
    expect(dialog().textContent).toContain(active.name);
    backend.expectNone('/api/meetings/7/cancel');
    dismiss().click();
    fixture.detectChanges();
    expect(dialog().open).toBeFalse();
    backend.expectNone('/api/meetings/7/cancel');
    expect(row().querySelector('.status')?.textContent).toBe('Etkin');
  });

  it('cancels a meeting once, prevents escape while pending, and refreshes its status', () => {
    flushList();
    open('cancel');
    confirm();
    submit().dispatchEvent(new Event('click'));
    const request = backend.expectOne('/api/meetings/7/cancel');
    expect(request.request.method).toBe('POST');
    expect(submit().disabled).toBeTrue();
    const escape = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(escape);
    expect(escape.defaultPrevented).toBeTrue();
    expect(dialog().open).toBeTrue();
    request.flush({ ...active, isCancelled: true });
    flushList([{ ...active, isCancelled: true }, cancelled]);
    expect(dialog().open).toBeFalse();
    expect(row().querySelector('[data-action="edit"]')).toBeNull();
    expect(page().textContent).toContain('Toplantı iptal edildi.');
  });

  it('permits escape before confirmation without mutating anything', () => {
    flushList();
    open('delete');
    dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
    fixture.detectChanges();
    expect(dialog().open).toBeFalse();
    backend.expectNone('/api/meetings/7');
  });

  it('returns to the previous server page when its last meeting is deleted', () => {
    flushList([active], 21);
    (page().querySelector('[data-page="next"]') as HTMLButtonElement).click();
    flushList([{ ...active, id: 21 }], 21, 2);
    open('delete', 21);
    expect(dialog().textContent).toContain('geri alınamaz');
    confirm();
    const request = backend.expectOne('/api/meetings/21');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    flushList([active], 20, 1);
    expect(page().querySelector('.pagination')?.textContent).toContain('Sayfa 1 / 1');
    expect(page().textContent).toContain('Toplantı silindi.');
  });

  it('deletes a document only after confirmation and refreshes document availability', () => {
    flushList();
    open('deleteDocument');
    backend.expectNone('/api/meetings/7/document');
    confirm();
    submit().dispatchEvent(new Event('click'));
    const request = backend.expectOne('/api/meetings/7/document');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    flushList([{ ...active, hasDocument: false, documentOriginalName: null }, cancelled]);
    expect(row().textContent).toContain('Belge yok');
    expect(action('download')).toBeNull();
    expect(row().textContent).toContain(active.name);
    expect(page().textContent).toContain('Belge silindi.');
  });

  it('handles a document deletion conflict with a safe explanation and refreshes a cancelled row', () => {
    flushList();
    open('deleteDocument');
    confirm();
    backend.expectOne('/api/meetings/7/document').flush({ detail: 'private conflict stack' },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(dialog().open).toBeTrue();
    expect(dialog().querySelector('[role="alert"]')?.textContent).toContain('iptal edilmiş olabilir');
    expect(dialog().textContent).not.toContain('private conflict');
    expect(submit().disabled).toBeTrue();
    expect(dismiss().textContent).toContain('listeyi yenile');
    dismiss().click();
    flushList([{ ...active, isCancelled: true }]);
    expect(action('deleteDocument')).toBeNull();
    expect(action('download')).not.toBeNull();
  });

  it('handles a meeting that disappears before confirmation and offers a list refresh', () => {
    flushList();
    open('delete');
    confirm();
    backend.expectOne('/api/meetings/7').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(dialog().textContent).toContain('artık bulunamıyor');
    expect(submit().disabled).toBeTrue();
    dismiss().click();
    flushList([]);
    expect(page().textContent).toContain('Henüz toplantınız yok');
  });

  it('keeps a failed mutation open and allows an explicit retry without duplicate requests', () => {
    flushList();
    open('cancel');
    confirm();
    backend.expectOne('/api/meetings/7/cancel').flush({ detail: 'secret exception' },
      { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(dialog().querySelector('[role="alert"]')?.textContent).toContain('İşlem tamamlanamadı');
    expect(dialog().textContent).not.toContain('secret exception');
    expect(submit().disabled).toBeFalse();
    confirm();
    backend.expectOne('/api/meetings/7/cancel').flush({ ...active, isCancelled: true });
    flushList([{ ...active, isCancelled: true }]);
  });

  it('downloads through the blob service, blocks duplicates and revokes the temporary URL', fakeAsync(() => {
    flushList();
    let filename = '';
    let href = '';
    anchorClick.and.callFake(function(this: HTMLAnchorElement) { filename = this.download; href = this.href; });
    action('download').click();
    action('download').dispatchEvent(new Event('click'));
    fixture.detectChanges();
    const request = backend.expectOne('/api/meetings/7/document');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(action('download').disabled).toBeTrue();
    const blob = new Blob(['meeting document'], { type: 'application/pdf' });
    request.flush(blob, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''g%C3%BCndem%20yeni.pdf" } });
    fixture.detectChanges();
    expect(objectUrl).toHaveBeenCalledWith(blob);
    expect(anchorClick).toHaveBeenCalledTimes(1);
    expect(filename).toBe('gündem yeni.pdf');
    expect(href).toBe('blob:meeting-document');
    expect(page().ownerDocument.querySelector('a[download]')).toBeNull();
    expect(action('download').disabled).toBeFalse();
    tick();
    expect(revokeUrl).toHaveBeenCalledOnceWith('blob:meeting-document');
  }));

  it('sanitizes path and control characters in download filenames', fakeAsync(() => {
    flushList([{ ...active, documentOriginalName: '../unsafe\\gün\u0001dem?.pdf' }]);
    let filename = '';
    anchorClick.and.callFake(function(this: HTMLAnchorElement) { filename = this.download; });
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob(['content']));
    expect(filename).toBe('gündem_.pdf');
    expect(filename).not.toMatch(/[\\/\u0000-\u001f]/);
    tick();
  }));

  it('uses the original filename when an encoded response filename is malformed', fakeAsync(() => {
    flushList();
    let filename = '';
    anchorClick.and.callFake(function(this: HTMLAnchorElement) { filename = this.download; });
    action('download', 8).click();
    backend.expectOne('/api/meetings/8/document').flush(new Blob(['content']), {
      headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%invalid" }
    });
    expect(filename).toBe('gündem.pdf');
    tick();
  }));

  it('uses a safe fallback when the document has no usable filename', fakeAsync(() => {
    flushList([{ ...active, documentOriginalName: '..' }]);
    let filename = '';
    anchorClick.and.callFake(function(this: HTMLAnchorElement) { filename = this.download; });
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob(['content']));
    expect(filename).toBe('toplanti-7-belge');
    tick();
  }));

  it('cleans an outstanding object URL on destruction before its deferred cleanup', fakeAsync(() => {
    flushList();
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob(['content']));
    expect(revokeUrl).not.toHaveBeenCalled();
    fixture.destroy();
    expect(revokeUrl).toHaveBeenCalledOnceWith('blob:meeting-document');
    tick();
    expect(revokeUrl).toHaveBeenCalledTimes(1);
  }));

  it('shows a safe download error and permits another attempt', fakeAsync(() => {
    flushList();
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob(['private storage path']),
      { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Belge artık bulunamıyor');
    expect(page().textContent).not.toContain('private storage');
    expect(action('download').disabled).toBeFalse();
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob(['content']));
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')).toBeNull();
    tick();
  }));

  it('does not create a download for an empty document', () => {
    flushList();
    action('download').click();
    backend.expectOne('/api/meetings/7/document').flush(new Blob([]));
    fixture.detectChanges();
    expect(page().querySelector('[role="alert"]')?.textContent).toContain('Belge boş');
    expect(objectUrl).not.toHaveBeenCalled();
    expect(anchorClick).not.toHaveBeenCalled();
  });
});

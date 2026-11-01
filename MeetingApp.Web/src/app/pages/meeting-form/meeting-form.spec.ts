import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { toLocalDateTimeInput } from '../../core/meetings/meeting-dates';
import { Meeting } from '../../core/meetings/meeting.models';
import { MeetingsService } from '../../core/meetings/meetings.service';
import { MeetingForm } from './meeting-form';

describe('Meeting form', () => {
  let fixture: ComponentFixture<MeetingForm>;
  let page: MeetingForm;
  let service: jasmine.SpyObj<MeetingsService>;
  let params: BehaviorSubject<ParamMap>;
  let router: Router;
  let destroyed: boolean;

  const meeting = (overrides: Partial<Meeting> = {}): Meeting => ({
    id: 7,
    name: 'Haftalık değerlendirme',
    description: 'Gündem ve kararlar',
    startsAtUtc: '2030-06-12T08:00:25.123Z',
    endsAtUtc: '2030-06-12T09:00:45.456Z',
    createdAtUtc: '2026-01-01T12:00:00Z',
    isCancelled: false,
    cancelledAtUtc: null,
    documentOriginalName: 'gündem.pdf',
    hasDocument: true,
    ...overrides
  });

  beforeEach(async () => {
    service = jasmine.createSpyObj<MeetingsService>('MeetingsService', ['getById', 'create', 'update']);
    service.getById.and.returnValue(of(meeting()));
    service.create.and.returnValue(of(meeting()));
    service.update.and.returnValue(of(meeting()));
    params = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [MeetingForm],
      providers: [
        provideRouter([]),
        { provide: MeetingsService, useValue: service },
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } }
      ]
    }).compileComponents();
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
    fixture = TestBed.createComponent(MeetingForm);
    page = fixture.componentInstance;
    destroyed = false;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (!destroyed) fixture.destroy();
    params.complete();
  });

  function validForm(): void {
    page.form.setValue({
      name: 'Planlama', description: 'Gündem', startsAt: '2001-01-02T10:00', endsAt: '2001-01-02T11:00'
    });
  }

  function edit(id = '7'): void {
    params.next(convertToParamMap({ id }));
    fixture.detectChanges();
  }

  function chooseFile(file: File | null): HTMLInputElement {
    const input = { value: file ? file.name : '', files: { item: () => file } } as unknown as HTMLInputElement;
    page.onDocumentChange({ target: input } as unknown as Event);
    return input;
  }

  function pdf(size = 10): File {
    const file = new File(['%PDF-test'], 'gündem.pdf', { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: size });
    return file;
  }

  it('shows required feedback and accessible field associations before sending a request', () => {
    page.submit();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('#meetingName-error')?.textContent).toContain('Toplantı adını girin.');
    expect(element.querySelector('#description-error')?.textContent).toContain('Toplantı açıklamasını girin.');
    expect(element.querySelector('#startsAt-error')?.textContent).toContain('Başlangıç tarihini ve saatini girin.');
    expect(element.querySelector('#endsAt-error')?.textContent).toContain('Bitiş tarihini ve saatini girin.');
    expect(element.querySelector('#meetingName')?.getAttribute('aria-invalid')).toBe('true');
    expect(element.querySelector('#meetingName')?.getAttribute('aria-describedby')).toBe('meetingName-error');
    expect(service.create).not.toHaveBeenCalled();
  });

  it('trims text, converts browser local dates to UTC, and allows historical meetings', () => {
    validForm();
    page.form.patchValue({ name: '  Planlama  ', description: '  Gündem\n  ' });
    page.submit();
    expect(service.create).toHaveBeenCalledOnceWith({
      name: 'Planlama', description: 'Gündem',
      startsAtUtc: new Date(2001, 0, 2, 10, 0).toISOString(),
      endsAtUtc: new Date(2001, 0, 2, 11, 0).toISOString(),
      document: null
    });
    expect(router.navigate).toHaveBeenCalledOnceWith(['/meetings'], {
      state: { meetingMessage: 'Toplantı oluşturuldu.' }
    });
  });

  it('rejects whitespace-only text and enforces the API text length limits', () => {
    validForm();
    page.form.patchValue({ name: '   ', description: ' \n ' });
    page.submit();
    expect(page.form.controls.name.hasError('required')).toBeTrue();
    expect(page.form.controls.description.hasError('required')).toBeTrue();
    page.form.patchValue({ name: 'a'.repeat(201), description: 'b'.repeat(4001) });
    page.submit();
    expect(page.form.controls.name.hasError('maxlength')).toBeTrue();
    expect(page.form.controls.description.hasError('maxlength')).toBeTrue();
    expect(service.create).not.toHaveBeenCalled();
  });

  it('accepts the exact 200 and 4000 character text limits', () => {
    validForm();
    page.form.patchValue({ name: 'a'.repeat(200), description: 'b'.repeat(4000) });
    page.submit();
    expect(service.create).toHaveBeenCalledTimes(1);
  });

  it('requires the end to follow the start and rejects invalid calendar dates', () => {
    validForm();
    for (const end of ['2001-01-02T10:00', '2001-01-02T09:00']) {
      page.form.controls.endsAt.setValue(end);
      page.submit();
      expect(page.fieldError('endsAt')).toContain('başlangıç tarihinden sonra');
    }
    page.form.patchValue({ startsAt: '2001-02-30T10:00', endsAt: '2001-03-01T11:00' });
    page.submit();
    expect(page.form.controls.startsAt.hasError('localDateTime')).toBeTrue();
    expect(service.create).not.toHaveBeenCalled();
  });

  it('loads an edit route with local dates and retains the existing document without a replacement', () => {
    edit();
    expect(service.getById).toHaveBeenCalledOnceWith(7);
    expect(page.form.controls.startsAt.value).toBe(toLocalDateTimeInput(meeting().startsAtUtc));
    expect(page.form.controls.endsAt.value).toBe(toLocalDateTimeInput(meeting().endsAtUtc));
    expect((fixture.nativeElement as HTMLElement).querySelector('.existing-document')?.textContent).toContain('gündem.pdf');
    page.form.controls.name.setValue('Yeni ad');
    page.submit();
    expect(service.create).not.toHaveBeenCalled();
    expect(service.update).toHaveBeenCalledOnceWith(7, {
      name: 'Yeni ad', description: 'Gündem ve kararlar',
      startsAtUtc: meeting().startsAtUtc, endsAtUtc: meeting().endsAtUtc, document: null
    });
    expect(router.navigate).toHaveBeenCalledOnceWith(['/meetings'], {
      state: { meetingMessage: 'Toplantı güncellendi.' }
    });
  });

  it('preserves original UTC precision when both times render in the same minute', () => {
    const original = meeting({ startsAtUtc: '2030-06-12T08:00:25.123Z', endsAtUtc: '2030-06-12T08:00:45.456Z' });
    service.getById.and.returnValue(of(original));
    edit();
    expect(page.form.controls.startsAt.value).toBe(page.form.controls.endsAt.value);
    expect(page.form.valid).toBeTrue();
    page.submit();
    expect(service.update.calls.mostRecent().args[1].startsAtUtc).toBe(original.startsAtUtc);
    expect(service.update.calls.mostRecent().args[1].endsAtUtc).toBe(original.endsAtUtc);
  });

  it('converts only the changed local input while preserving the other original UTC instant', () => {
    edit();
    page.form.controls.startsAt.setValue('2001-01-02T10:00');
    page.submit();
    const input = service.update.calls.mostRecent().args[1];
    expect(input.startsAtUtc).toBe(new Date(2001, 0, 2, 10, 0).toISOString());
    expect(input.endsAtUtc).toBe(meeting().endsAtUtc);
  });

  it('preserves original timestamps after an input is changed and then restored', () => {
    edit();
    const originalLocal = page.form.controls.startsAt.value;
    page.form.controls.startsAt.setValue('2001-01-02T10:00');
    page.form.controls.startsAt.setValue(originalLocal);
    page.submit();
    expect(service.update.calls.mostRecent().args[1].startsAtUtc).toBe(meeting().startsAtUtc);
  });

  it('keeps cancelled meetings read-only and never calls update', () => {
    service.getById.and.returnValue(of(meeting({ isCancelled: true })));
    edit();
    page.submit();
    expect(service.update).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('fieldset')?.disabled).toBeTrue();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Bu toplantı iptal edildi.');
  });

  it('accepts a PDF or DOCX replacement without creating a preview URL', () => {
    const createUrl = spyOn(URL, 'createObjectURL');
    edit();
    const replacement = new File(['docx-content'], 'GÜNDEM.DOCX', {
      type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
    });
    chooseFile(replacement);
    page.submit();
    expect(service.update.calls.mostRecent().args[1].document).toBe(replacement);
    expect(createUrl).not.toHaveBeenCalled();
  });

  it('clears a previous selection and the file input when the next file has an invalid type', () => {
    chooseFile(pdf());
    const input = chooseFile(new File(['text'], 'note.txt', { type: 'text/plain' }));
    expect(page.document()).toBeNull();
    expect(input.value).toBe('');
    expect(page.documentError()).toContain('PDF veya DOCX');
    validForm();
    page.submit();
    expect(service.create).not.toHaveBeenCalled();
  });

  it('rejects an empty file, a misleading MIME type and files above 10 MiB', () => {
    for (const file of [pdf(0), new File(['text'], 'note.pdf', { type: 'text/plain' }), pdf(10 * 1024 * 1024 + 1)]) {
      const input = chooseFile(file);
      expect(page.document()).toBeNull();
      expect(page.documentError()).not.toBe('');
      expect(input.value).toBe('');
    }
  });

  it('accepts the exact 10 MiB limit and permits clearing a replacement while retaining the existing document', () => {
    edit();
    const input = chooseFile(pdf(10 * 1024 * 1024));
    expect(page.documentError()).toBe('');
    expect(page.document()).not.toBeNull();
    page.removeDocument(input);
    expect(input.value).toBe('');
    expect(page.document()).toBeNull();
    expect(page.meeting()?.hasDocument).toBeTrue();
    page.submit();
    expect(service.update.calls.mostRecent().args[1].document).toBeNull();
  });

  it('prevents duplicate saves and protects inputs while the request is pending', () => {
    const pending = new Subject<Meeting>();
    service.create.and.returnValue(pending);
    validForm();
    page.submit();
    page.submit();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(service.create).toHaveBeenCalledTimes(1);
    expect((element.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((element.querySelector('#meetingName') as HTMLInputElement).readOnly).toBeTrue();
    expect((element.querySelector('#document') as HTMLInputElement).disabled).toBeTrue();
    expect(element.querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    pending.next(meeting());
    pending.complete();
    page.submit();
    expect(page.saving()).toBeFalse();
    expect(service.create).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledTimes(1);
  });

  it('maps only known 400 fields to friendly feedback and never exposes server details', () => {
    service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400, error: {
      errors: { Name: ['private-name'], 'request.EndsAtUtc': ['private-date'], Document: ['private-document'],
        Unknown: ['<script>private-stack</script>'] }, detail: 'private-trace'
    } })));
    validForm();
    page.submit();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(page.fieldError('name')).toContain('200 karakter');
    expect(page.fieldError('endsAt')).toContain('başlangıç tarihinden sonra');
    expect(page.documentError()).toContain('PDF veya DOCX');
    expect(text).not.toContain('private-');
    expect(page.saving()).toBeFalse();
  });

  it('handles malformed validation payloads safely and lets the user retry', () => {
    service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400, error: '<html>private-detail</html>' })));
    validForm();
    page.submit();
    expect(page.errorMessage()).toContain('Bilgilerinizi kontrol');
    expect(page.serverErrors()).toEqual({});
    service.create.and.returnValue(of(meeting()));
    page.form.controls.name.setValue('Düzeltilmiş ad');
    page.submit();
    expect(service.create).toHaveBeenCalledTimes(2);
  });

  it('reports a server upload limit and allows clearing the file error before retrying', () => {
    service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 413, error: 'private-detail' })));
    validForm();
    chooseFile(pdf());
    page.submit();
    expect(page.documentError()).toContain('En fazla 10 MB');
    expect(page.errorMessage()).toBe('Yüklenen dosya çok büyük.');
    page.removeDocument();
    service.create.and.returnValue(of(meeting()));
    page.submit();
    expect(service.create).toHaveBeenCalledTimes(2);
  });

  it('blocks resubmission after a conflict without treating the meeting as cancelled', () => {
    service.update.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409, error: 'private-stale-document-detail' })));
    edit();
    page.submit();
    page.submit();
    fixture.detectChanges();
    expect(page.readOnly()).toBeTrue();
    expect(page.conflict()).toBeTrue();
    expect(page.meeting()?.isCancelled).toBeFalse();
    expect(page.errorMessage()).toContain('başka bir işlem nedeniyle değişmiş olabilir');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('kaydedilmemiş değişikliklerinizi siler');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('private-stale-document-detail');
    expect(service.update).toHaveBeenCalledTimes(1);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('reloads the current meeting after a conflict and clears unsaved input before allowing a new save', () => {
    service.update.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    edit();
    page.form.controls.name.setValue('Kaydedilmemiş ad');
    chooseFile(pdf());
    page.submit();
    service.getById.and.returnValue(of(meeting({ name: 'Sunucudaki son ad', hasDocument: false, documentOriginalName: null })));
    page.retry();
    fixture.detectChanges();
    expect(service.getById).toHaveBeenCalledTimes(2);
    expect(service.getById.calls.mostRecent().args).toEqual([7]);
    expect(page.form.controls.name.value).toBe('Sunucudaki son ad');
    expect(page.document()).toBeNull();
    expect(page.conflict()).toBeFalse();
    expect(page.errorMessage()).toBe('');
    expect(page.readOnly()).toBeFalse();
    service.update.and.returnValue(of(meeting()));
    page.submit();
    expect(service.update).toHaveBeenCalledTimes(2);
  });

  it('keeps a meeting read-only when a conflict reload returns a cancelled entity', () => {
    service.update.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    edit();
    page.submit();
    service.getById.and.returnValue(of(meeting({ isCancelled: true })));
    page.retry();
    page.submit();
    expect(page.conflict()).toBeFalse();
    expect(page.meeting()?.isCancelled).toBeTrue();
    expect(page.readOnly()).toBeTrue();
    expect(service.update).toHaveBeenCalledTimes(1);
  });

  it('hides stale edit content on a missing or inaccessible meeting', () => {
    edit();
    service.getById.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404, error: 'private-owner-detail' })));
    edit('8');
    expect(page.meeting()).toBeNull();
    expect(page.form.controls.name.value).toBe('');
    expect(page.errorMessage()).toContain('Toplantı bulunamadı');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('gündem.pdf');
    page.submit();
    expect(service.update).not.toHaveBeenCalled();
  });

  it('reports network failure safely and reloads the same meeting on retry', () => {
    service.getById.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') })));
    edit();
    expect(page.errorMessage()).toContain('hizmetine ulaşılamıyor');
    expect(page.canRetry()).toBeTrue();
    service.getById.and.returnValue(of(meeting()));
    page.retry();
    fixture.detectChanges();
    expect(service.getById).toHaveBeenCalledTimes(2);
    expect(page.unavailable()).toBeFalse();
    expect(page.form.controls.name.value).toBe(meeting().name);
  });

  it('reports save network failures and 401 responses without exposing raw details or navigating itself', () => {
    validForm();
    for (const status of [0, 401]) {
      service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status, error: 'private-detail' })));
      page.submit();
      expect(page.errorMessage()).not.toContain('private-detail');
      expect(page.saving()).toBeFalse();
    }
    expect(page.errorMessage()).toContain('tekrar giriş yapın');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('validates route identifiers before requesting a meeting and clears prior form data', () => {
    edit();
    service.getById.calls.reset();
    chooseFile(pdf());
    for (const id of ['0', '-1', '1.5', '1e3', 'abc', '9007199254740992']) {
      edit(id);
      expect(page.unavailable()).toBeTrue();
      expect(page.meeting()).toBeNull();
      expect(page.document()).toBeNull();
      expect(page.form.controls.name.value).toBe('');
    }
    expect(service.getById).not.toHaveBeenCalled();
    page.submit();
    expect(service.create).not.toHaveBeenCalled();
    expect(service.update).not.toHaveBeenCalled();
  });

  it('cancels an old load on parameter changes and ignores its late data', () => {
    const first = new Subject<Meeting>();
    const second = new Subject<Meeting>();
    service.getById.and.callFake(id => id === 7 ? first : second);
    edit();
    expect(page.loading()).toBeTrue();
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
    edit('8');
    expect(first.observed).toBeFalse();
    first.next(meeting());
    expect(page.loading()).toBeTrue();
    expect(page.meeting()).toBeNull();
    second.next(meeting({ id: 8, name: 'Doğru toplantı', hasDocument: false, documentOriginalName: null }));
    second.complete();
    fixture.detectChanges();
    expect(page.meeting()?.id).toBe(8);
    expect(page.form.controls.name.value).toBe('Doğru toplantı');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('gündem.pdf');
  });

  it('clears edit data and a pending save when the route changes to new', () => {
    const pending = new Subject<Meeting>();
    service.update.and.returnValue(pending);
    edit();
    chooseFile(pdf());
    page.submit();
    params.next(convertToParamMap({}));
    fixture.detectChanges();
    expect(pending.observed).toBeFalse();
    expect(page.editing()).toBeFalse();
    expect(page.saving()).toBeFalse();
    expect(page.document()).toBeNull();
    expect(page.meeting()).toBeNull();
    expect(page.form.controls.name.value).toBe('');
    pending.next(meeting());
    expect(router.navigate).not.toHaveBeenCalled();
    validForm();
    page.submit();
    expect(service.create).toHaveBeenCalledTimes(1);
  });

  it('cancels pending requests when the component is destroyed', () => {
    const pending = new Subject<Meeting>();
    service.create.and.returnValue(pending);
    validForm();
    page.submit();
    fixture.destroy();
    destroyed = true;
    expect(pending.observed).toBeFalse();
    expect(page.saving()).toBeFalse();
    pending.next(meeting());
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('provides list links and browser timezone guidance without imposing a future-only minimum', () => {
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.back-link')?.getAttribute('href')).toBe('/meetings');
    expect(element.querySelector('.secondary-link')?.getAttribute('href')).toBe('/meetings');
    expect(element.querySelector('#time-zone')?.textContent).toContain(page.timeZone);
    expect(element.querySelector('#startsAt')?.hasAttribute('min')).toBeFalse();
    expect(element.querySelector('#endsAt')?.hasAttribute('min')).toBeFalse();
  });
});

import { HttpResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authInterceptor } from '../auth/auth.interceptor';
import { AuthService } from '../auth/auth.service';
import { Meeting, MeetingInput, MeetingListResponse } from './meeting.models';
import { MeetingsService } from './meetings.service';

describe('MeetingsService API contract', () => {
  let service: MeetingsService;
  let http: HttpTestingController;
  const meeting: Meeting = {
    id: 17,
    name: 'Tasarım görüşmesi',
    description: 'Gündem ve kararlar',
    startsAtUtc: '2026-11-10T09:15:32.1234567Z',
    endsAtUtc: '2026-11-10T10:15:45.7654321Z',
    createdAtUtc: '2026-11-01T08:00:00Z',
    isCancelled: false,
    cancelledAtUtc: null,
    documentOriginalName: 'gündem.pdf',
    hasDocument: true
  };
  const input = (document: File | null = null): MeetingInput => ({
    name: meeting.name,
    description: meeting.description,
    startsAtUtc: meeting.startsAtUtc,
    endsAtUtc: meeting.endsAtUtc,
    document
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { getAccessToken: () => 'meeting.jwt.token' } },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) }
      ]
    });
    service = TestBed.inject(MeetingsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the first 20 meetings and returns server pagination unchanged', () => {
    const response: MeetingListResponse = { items: [meeting], totalCount: 43, page: 1, pageSize: 20 };
    let received: MeetingListResponse | undefined;
    service.list().subscribe(value => received = value);
    const request = http.expectOne('/api/meetings?page=1&pageSize=20');
    expect(request.request.method).toBe('GET');
    request.flush(response);
    expect(received).toEqual(response);
  });

  it('sends the requested server page rather than downloading and slicing meetings', () => {
    service.list(3, 20).subscribe();
    const request = http.expectOne('/api/meetings?page=3&pageSize=20');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], totalCount: 40, page: 3, pageSize: 20 });
  });

  it('loads an individual meeting from its owner-protected endpoint', () => {
    let received: Meeting | undefined;
    service.getById(17).subscribe(value => received = value);
    const request = http.expectOne('/api/meetings/17');
    expect(request.request.method).toBe('GET');
    request.flush(meeting);
    expect(received).toEqual(meeting);
  });

  it('creates multipart data with exact field names, original UTC precision and an optional file', () => {
    const file = new File(['%PDF-fixture'], 'gündem.pdf', { type: 'application/pdf' });
    let received: Meeting | undefined;
    service.create(input(file)).subscribe(value => received = value);
    const request = http.expectOne('/api/meetings');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBeTrue();
    const data = request.request.body as FormData;
    expect(Array.from(data.keys())).toEqual(['Name', 'Description', 'StartsAtUtc', 'EndsAtUtc', 'Document']);
    expect(data.get('Name')).toBe(meeting.name);
    expect(data.get('Description')).toBe(meeting.description);
    expect(data.get('StartsAtUtc')).toBe(meeting.startsAtUtc);
    expect(data.get('EndsAtUtc')).toBe(meeting.endsAtUtc);
    const uploaded = data.get('Document') as File;
    expect(uploaded.name).toBe('gündem.pdf');
    expect(uploaded.size).toBe(file.size);
    expect(uploaded.type).toBe('application/pdf');
    expect(request.request.headers.has('Content-Type')).toBeFalse();
    expect(request.request.headers.get('Authorization')).toBe('Bearer meeting.jwt.token');
    request.flush(meeting);
    expect(received).toEqual(meeting);
  });

  it('omits Document when creating a meeting without an attachment', () => {
    service.create(input()).subscribe();
    const request = http.expectOne('/api/meetings');
    expect((request.request.body as FormData).has('Document')).toBeFalse();
    request.flush({ ...meeting, documentOriginalName: null, hasDocument: false });
  });

  it('updates text and exact UTC instants without sending an empty document replacement', () => {
    service.update(17, input()).subscribe();
    const request = http.expectOne('/api/meetings/17');
    expect(request.request.method).toBe('PUT');
    const data = request.request.body as FormData;
    expect(Array.from(data.keys())).toEqual(['Name', 'Description', 'StartsAtUtc', 'EndsAtUtc']);
    expect(data.get('Name')).toBe(meeting.name);
    expect(data.get('Description')).toBe(meeting.description);
    expect(data.get('StartsAtUtc')).toBe(meeting.startsAtUtc);
    expect(data.get('EndsAtUtc')).toBe(meeting.endsAtUtc);
    expect(request.request.headers.has('Content-Type')).toBeFalse();
    request.flush(meeting);
  });

  it('sends a selected replacement attachment with the update', () => {
    const file = new File(['docx-fixture'], 'yeni-gündem.docx', {
      type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
    });
    service.update(17, input(file)).subscribe();
    const request = http.expectOne('/api/meetings/17');
    expect(request.request.method).toBe('PUT');
    expect(((request.request.body as FormData).get('Document') as File).name).toBe(file.name);
    expect(request.request.headers.has('Content-Type')).toBeFalse();
    request.flush({ ...meeting, documentOriginalName: file.name });
  });

  it('cancels through POST and returns the updated meeting status', () => {
    let received: Meeting | undefined;
    service.cancel(17).subscribe(value => received = value);
    const request = http.expectOne('/api/meetings/17/cancel');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    const cancelled = { ...meeting, isCancelled: true, cancelledAtUtc: '2026-11-01T10:00:00Z' };
    request.flush(cancelled);
    expect(received).toEqual(cancelled);
  });

  it('deletes a meeting and accepts an empty 204 response', () => {
    let completed = false;
    service.delete(17).subscribe({ complete: () => completed = true });
    const request = http.expectOne('/api/meetings/17');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(completed).toBeTrue();
  });

  it('downloads a blob through the bearer interceptor and retains response headers', () => {
    const document = new Blob(['%PDF-fixture'], { type: 'application/pdf' });
    let received: HttpResponse<Blob> | undefined;
    service.downloadDocument(17).subscribe(value => received = value);
    const request = http.expectOne('/api/meetings/17/document');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.headers.get('Authorization')).toBe('Bearer meeting.jwt.token');
    request.flush(document, { headers: { 'Content-Disposition': 'attachment; filename="agenda.pdf"' } });
    expect(received?.body).toBe(document);
    expect(received?.headers.get('Content-Disposition')).toBe('attachment; filename="agenda.pdf"');
  });

  it('deletes only the attached document through its separate endpoint', () => {
    let completed = false;
    service.deleteDocument(17).subscribe({ complete: () => completed = true });
    const request = http.expectOne('/api/meetings/17/document');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(completed).toBeTrue();
  });

  it('lets the form handle a cancelled-meeting conflict without retrying the mutation', () => {
    let status: number | undefined;
    service.update(17, input()).subscribe({ error: error => status = error.status });
    http.expectOne('/api/meetings/17').flush({ message: 'Toplantı iptal edilmiş.' }, {
      status: 409,
      statusText: 'Conflict'
    });
    expect(status).toBe(409);
    http.expectNone('/api/meetings/17');
  });
});

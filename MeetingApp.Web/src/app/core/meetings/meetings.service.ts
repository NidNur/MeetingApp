import { HttpClient, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Meeting, MeetingInput, MeetingListResponse } from './meeting.models';

@Injectable({ providedIn: 'root' })
export class MeetingsService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/meetings';

  list(page = 1, pageSize = 20): Observable<MeetingListResponse> {
    return this.http.get<MeetingListResponse>(this.url, { params: { page, pageSize } });
  }

  getById(id: number): Observable<Meeting> {
    return this.http.get<Meeting>(`${this.url}/${id}`);
  }

  create(input: MeetingInput): Observable<Meeting> {
    return this.http.post<Meeting>(this.url, this.toFormData(input));
  }

  update(id: number, input: MeetingInput): Observable<Meeting> {
    return this.http.put<Meeting>(`${this.url}/${id}`, this.toFormData(input));
  }

  cancel(id: number): Observable<Meeting> {
    return this.http.post<Meeting>(`${this.url}/${id}/cancel`, null);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }

  downloadDocument(id: number): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.url}/${id}/document`, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  deleteDocument(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}/document`);
  }

  private toFormData(input: MeetingInput): FormData {
    const data = new FormData();
    data.append('Name', input.name);
    data.append('Description', input.description);
    // The form already converted local input or retained the original UTC instant.
    data.append('StartsAtUtc', input.startsAtUtc);
    data.append('EndsAtUtc', input.endsAtUtc);
    if (input.document) data.append('Document', input.document, input.document.name);
    return data;
  }
}

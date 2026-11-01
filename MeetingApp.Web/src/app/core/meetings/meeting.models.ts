export interface Meeting {
  id: number;
  name: string;
  description: string;
  startsAtUtc: string;
  endsAtUtc: string;
  createdAtUtc: string;
  isCancelled: boolean;
  cancelledAtUtc: string | null;
  documentOriginalName: string | null;
  hasDocument: boolean;
}

export interface MeetingListResponse {
  items: Meeting[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface MeetingInput {
  name: string;
  description: string;
  startsAtUtc: string;
  endsAtUtc: string;
  document: File | null;
}

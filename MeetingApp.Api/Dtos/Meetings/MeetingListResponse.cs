namespace MeetingApp.Api.Dtos.Meetings;

public class MeetingListResponse
{
    public IReadOnlyList<MeetingResponse> Items { get; set; } = [];

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

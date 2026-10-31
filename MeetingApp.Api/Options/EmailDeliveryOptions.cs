using System.ComponentModel.DataAnnotations;

namespace MeetingApp.Api.Options;

public class EmailDeliveryOptions
{
    public bool Enabled { get; set; } = true;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 20;

    [Range(1, 3600)]
    public int RetryDelaySeconds { get; set; } = 30;
}

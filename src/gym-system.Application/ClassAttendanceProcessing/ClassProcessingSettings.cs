namespace gym_system.Application.ClassAttendanceProcessing;

public sealed class ClassProcessingSettings
{
    public bool Enabled { get; set; }
    public DateOnly? StartDate { get; set; }
    public int PollIntervalSeconds { get; set; } = 5;
    public void Validate()
    {
        if (Enabled && StartDate is null) throw new InvalidOperationException("ClassProcessing:StartDate is required when enabled.");
        if (PollIntervalSeconds is < 1 or > 60) throw new InvalidOperationException("ClassProcessing poll interval must be 1 to 60 seconds.");
    }
}

public interface IClassProcessingErrorPolicy
{
    bool IsTransient(Exception exception);
}

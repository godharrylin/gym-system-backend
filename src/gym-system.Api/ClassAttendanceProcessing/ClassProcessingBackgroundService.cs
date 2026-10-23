using gym_system.Application.ClassAttendanceProcessing;
using gym_system.Infrastructures.ClassAttendanceProcessing;

namespace gym_system.Api.ClassAttendanceProcessing;

public sealed class ClassProcessingBackgroundService(IServiceScopeFactory scopes,
    ClassProcessingSettings settings, ILogger<ClassProcessingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) return;
        settings.Validate();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ClassProcessingWorker>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Class processing tick failed; persisted work remains recoverable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

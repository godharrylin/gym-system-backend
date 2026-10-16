namespace gym_system.Application.GymAccessUseCase;

/// <summary>
/// Internal stage-01 identity/basic-ticket checks only. No login, device authorization,
/// class-window permission, reservation, attendance, gate command or visit is created.
/// Future entry orchestration MUST perform device/class/reservation checks separately.
/// </summary>
public interface IGymAccessEligibilityService
{
    Task<GymAccessEligibilityResult> CheckEntryAsync(string? phone, CancellationToken ct = default);
    Task<GymAccessEligibilityResult> CheckExitAsync(string? phone, CancellationToken ct = default);
}

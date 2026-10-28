namespace gym_system.Application.GymAccessUseCase;

/// <summary>
/// Internal stage-01 identity/basic-ticket checks only. No login, device authorization,
/// class-window permission, reservation, attendance, gate command or visit is created.
/// Future entry orchestration MUST perform device/class/reservation checks separately.
/// </summary>
public interface IGymAccessEligibilityService
{
    // Read-only identity/profile checks for gate orchestration. Ticket reconciliation must
    // wait until class-window and previous-settlement checks inside the caller transaction.
    Task<GymAccessEligibilityResult> IdentifyEntryAsync(string? phone, CancellationToken ct = default);
    Task<GymAccessEligibilityResult> CheckEntryAsync(string? phone, CancellationToken ct = default);
    Task<GymAccessEligibilityResult> CheckExitAsync(string? phone, CancellationToken ct = default);
}

using gym_system.Application.TicketsUseCase.Queries;
using gym_system.Application.TicketsUseCase.Services;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace gym_system.Application.GymAccessUseCase;

public sealed class GymAccessEligibilityService(
    IUserRepository users,
    IUserRoleRepository roles,
    IStudentProfileRepository profiles,
    ICurrentTicketService currentTickets,
    IStudentTicketPassQueryService ticketHistory,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<GymAccessEligibilityService> logger) : IGymAccessEligibilityService
{
    public async Task<GymAccessEligibilityResult> CheckEntryAsync(string? phone, CancellationToken ct = default)
    {
        var now = clock.Now();
        var (user, error) = await IdentifyAsync(phone, now, ct);
        if (error is not null) return error;
        var member = user!;
        if (!member.IsActive)
            return Reject("GYM_ACCOUNT_INACTIVE", "帳號已停用，請洽工作人員", now, member.Id);

        var activeRoles = await GetRolesAsync(member.Id, ct);
        if (activeRoles.Any(IsStaff))
            return Passed("GYM_STAFF_BASIC_ELIGIBLE", "工作人員身分確認完成", now,
                member.Id, GymAccessIdentityKind.Staff, activeRoles);
        if (!activeRoles.Contains(UserRoleCode.Student))
            return Reject("GYM_ROLE_NOT_ELIGIBLE", "尚未註冊會員", now, member.Id, activeRoles);
        if (await profiles.FindByUserIdAsync(member.Id, ct) is null)
            return Reject("GYM_STUDENT_PROFILE_MISSING", "會員資料異常，請洽工作人員", now,
                member.Id, activeRoles);

        // Own one transaction here. The shared lifecycle service never starts another.
        CurrentTicketSnapshot? ticket;
        await unitOfWork.BeginAsync(ct);
        try
        {
            ticket = await currentTickets.ReconcileAsync(member.Id, now, "SYSTEM", ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (ticket is null)
        {
            // History only explains a refusal. It NEVER grants authorization, and is read
            // after commit because the query service uses its own SQL connection.
            var history = await ticketHistory.GetByStudentIdAsync(member.Id, ct);
            var (code, message) = DescribeUnavailable(history.FirstOrDefault());
            return Reject(code, message, now, member.Id, activeRoles);
        }
        var issue = ValidateTicket(ticket, DateOnly.FromDateTime(now));
        if (issue is not null)
            return Reject(issue.Value.Code, issue.Value.Message, now, member.Id, activeRoles);

        return Passed("GYM_STUDENT_BASIC_ELIGIBLE", "基本票券資格通過，仍須驗證課程與額度占用", now,
            member.Id, GymAccessIdentityKind.Student, activeRoles) with { CurrentTicket = ticket };
    }

    public async Task<GymAccessEligibilityResult> CheckExitAsync(string? phone, CancellationToken ct = default)
    {
        var now = clock.Now();
        var (user, error) = await IdentifyAsync(phone, now, ct);
        if (error is not null) return error;
        var member = user!;
        var warnings = new List<string>();
        if (!member.IsActive) warnings.Add("GYM_ACCOUNT_INACTIVE");
        IReadOnlyList<UserRoleCode> activeRoles = [];
        try
        {
            activeRoles = await GetRolesAsync(member.Id, ct);
            if (!activeRoles.Any(IsStaff) && !activeRoles.Contains(UserRoleCode.Student))
                warnings.Add("GYM_ROLE_NOT_ELIGIBLE");
            if (activeRoles.Contains(UserRoleCode.Student) && !activeRoles.Any(IsStaff))
            {
                if (await profiles.FindByUserIdAsync(member.Id, ct) is null)
                    warnings.Add("GYM_STUDENT_PROFILE_MISSING");
                // Exit diagnostics are read-only: never activate a ticket just to exit.
                var history = await ticketHistory.GetByStudentIdAsync(member.Id, ct);
                var active = history.FirstOrDefault(x => x.ValidStatus.Equals("Active", StringComparison.OrdinalIgnoreCase));
                if (active is null)
                    warnings.Add(DescribeUnavailable(history.FirstOrDefault()).Code);
                else
                {
                    var issue = ValidateTicket(new CurrentTicketSnapshot
                    {
                        TicketId = active.PassId, PlanCode = active.PlanCode,
                        TicketType = active.PlanType, TicketValidState = active.ValidStatus,
                        TicketPaymentState = active.PaymentStatus,
                        TicketStartDate = active.ValidStartDate is DateTime start ? DateOnly.FromDateTime(start) : null,
                        TicketExpireDate = active.ValidEndDate is DateTime end ? DateOnly.FromDateTime(end) : null,
                        TicketRemainCount = active.CreditsRemaining
                    }, DateOnly.FromDateTime(now));
                    if (issue is not null) warnings.Add(issue.Value.Code);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Once identified, failing optional diagnosis must not become a ticket-based exit denial.
            warnings.Add("GYM_EXIT_DIAGNOSIS_UNAVAILABLE");
            logger.LogError(ex, "Gym exit diagnosis unavailable. UserId={UserId}", member.Id);
        }
        foreach (var warning in warnings)
            logger.LogWarning("Gym exit anomaly. UserId={UserId} Code={Code}", member.Id, warning);
        return Passed("GYM_EXIT_IDENTIFIED", "身分確認完成，可交由出場流程處理", now,
            member.Id, GymAccessIdentityKind.IdentifiedUser, activeRoles) with { WarningCodes = warnings.AsReadOnly() };
    }

    private async Task<(User? User, GymAccessEligibilityResult? Error)> IdentifyAsync(
        string? phone, DateTime now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = phone?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return (null, Reject("GYM_PHONE_REQUIRED", "請輸入手機號碼", now));
        if (normalized.Length != 10 || !normalized.StartsWith("09", StringComparison.Ordinal)
            || normalized.Any(x => x is < '0' or > '9'))
            return (null, Reject("GYM_PHONE_INVALID", "請輸入 09 開頭的十位台灣手機號碼", now));
        var user = await users.FindUserByPhoneAsync(normalized, ct);
        return user is null
            ? (null, Reject("GYM_USER_NOT_FOUND", "尚未註冊會員", now))
            : (user, null);
    }

    private async Task<IReadOnlyList<UserRoleCode>> GetRolesAsync(string userId, CancellationToken ct) =>
        (await roles.GetActiveRolesAsync(userId, ct)).Where(x => x.IsActive)
            .Select(x => x.RoleCode).Distinct().OrderBy(x => x).ToArray();

    private static bool IsStaff(UserRoleCode role) =>
        role is UserRoleCode.Admin or UserRoleCode.Staff or UserRoleCode.Instructor;

    private static (string Code, string Message)? ValidateTicket(CurrentTicketSnapshot ticket, DateOnly today)
    {
        if (!ticket.TicketValidState.Equals("Active", StringComparison.OrdinalIgnoreCase)
            || !ticket.TicketPaymentState.Equals("Paid", StringComparison.OrdinalIgnoreCase))
            return ("GYM_NO_USABLE_TICKET", "目前沒有可使用的已付款票券");
        if (ticket.TicketStartDate > today)
            return ("GYM_TICKET_NOT_ACTIVE", "票券尚未到啟用日期");
        if (ticket.TicketExpireDate < today)
            return ("GYM_TICKET_EXPIRED", "該會員票券已過期");
        var pack = ticket.TicketType.Equals("PACK", StringComparison.OrdinalIgnoreCase);
        var monthly = ticket.TicketType.Equals("M_PASS", StringComparison.OrdinalIgnoreCase);
        var single = ticket.PlanCode.Equals("SINGLE", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(ticket.TicketId) || string.IsNullOrWhiteSpace(ticket.PlanCode)
            || (!pack && !monthly) || (single && !pack)
            || (!single && (ticket.TicketStartDate is null || ticket.TicketExpireDate is null))
            || (pack && ticket.TicketRemainCount is null))
            return ("GYM_TICKET_DATA_INVALID", "會員票券資料異常，請洽工作人員");
        if (pack && ticket.TicketRemainCount <= 0)
            return ("GYM_TICKET_DEPLETED", "票券堂數已用完");
        return null;
    }

    private static (string Code, string Message) DescribeUnavailable(StudentTicketPassResult? latest) =>
        latest?.ValidStatus.ToUpperInvariant() switch
        {
            null => ("GYM_NO_TICKET", "目前沒有可使用的已付款票券"),
            "EXPIRE" => ("GYM_TICKET_EXPIRED", "該會員票券已過期"),
            "CANCELLED" => ("GYM_TICKET_CANCELLED", "票券已取消，目前沒有可用票券"),
            "DEPLETED" => ("GYM_TICKET_DEPLETED", "票券堂數已用完"),
            "UNACTIVE" => ("GYM_TICKET_NOT_ACTIVE", "票券尚未符合啟用條件"),
            _ => ("GYM_NO_USABLE_TICKET", "目前沒有可使用的已付款票券")
        };

    private GymAccessEligibilityResult Reject(string code, string message, DateTime now,
        string? userId = null, IReadOnlyList<UserRoleCode>? activeRoles = null)
    {
        // Do not log customer phones, passwords or authentication tokens.
        logger.LogWarning("Gym basic eligibility rejected. UserId={UserId} Code={Code}", userId, code);
        return new() { BasicEligibilityPassed = false, Code = code, Message = message, EvaluatedAt = now,
            UserId = userId, ActiveRoles = activeRoles ?? [] };
    }

    private static GymAccessEligibilityResult Passed(string code, string message, DateTime now,
        string userId, GymAccessIdentityKind identity, IReadOnlyList<UserRoleCode> activeRoles) =>
        new() { BasicEligibilityPassed = true, Code = code, Message = message, EvaluatedAt = now,
            UserId = userId, IdentityKind = identity, ActiveRoles = activeRoles };
}

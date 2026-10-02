using gym_system.Domain.Repositories;

namespace gym_system.Application.MembersUseCase.Queries.GetStudentMemberList;

public sealed class GetStudentMemberListHandler(IStudentMemberListQueryService queries, IClock clock)
{
    public Task<StudentMemberListResult> Handle(GetStudentMemberListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ArgumentException("分頁參數不合法（每頁 1–100 筆）");
        if ((query.Keyword?.Trim().Length ?? 0) > 100)
            throw new ArgumentException("搜尋文字不可超過 100 字");
        string[] valid = ["Active", "Expiring", "UnActive", "Expire", "Depleted", "Cancelled"];
        string[] payment = ["Paid", "UnPaid", "Cancel"];
        if (query.ValidStates.Any(x => !valid.Contains(x)) || query.PaymentStates.Any(x => !payment.Contains(x)))
            throw new ArgumentException("不支援的篩選狀態");
        return queries.QueryAsync(query, clock.Today(), ct);
    }
}

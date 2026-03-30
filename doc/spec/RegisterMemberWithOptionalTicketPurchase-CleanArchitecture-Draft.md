# Register Member with Optional Ticket Purchase - Clean Architecture 草稿

## 1. 目標
在會員註冊時，支援「可選擇購買票券」。

- 純註冊: 只建立 `users` + `sdt_profile`
- 註冊加購票券: 同交易內建立 `users` + `sdt_profile` + `orders` + `order_items` + `sdt_ticket_pass`

資料來源以 `doc/Database.md` 與 `doc/api/註冊會員頁面API規格.md` 為準。

## 2. 分層職責

### API Layer
- 接收 `POST /api/v1/users/register`
- 轉成 Application Command
- 回傳成功/失敗結果

### Application Layer (Use Case)
- 驗證輸入
- 執行 phone 唯一性檢查
- 情境分流 (純註冊 / 註冊+購票)
- 管理交易邊界
- 呼叫 Domain 規則計算應收/實收
- 協調多個 Repository 寫入

### Domain Layer
- Entity/ValueObject/Enum
- 商業規則:
  - Family Plan 折扣規則 (人數 >= 2 折 5%)
  - 票券到期日推算規則 (`default_expire_days = -1` 表示無期限)
  - 票券初始狀態規則 (`activationDate` 與 today 比較決定 `Active`/`UnActive`)

### Infrastructure Layer
- Repository 實作 (SQL)
- Unit of Work / Transaction 實作
- DB unique constraint 與例外轉譯

## 3. Command / DTO 草稿

```csharp
public sealed class RegisterMembersCommand
{
    public IReadOnlyList<MemberInput> Members { get; init; } = Array.Empty<MemberInput>();
    public TicketPurchaseInput? TicketPurchase { get; init; }
    public string OperatorId { get; init; } = string.Empty; // 後台操作人員
}

public sealed class MemberInput
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
}

public sealed class TicketPurchaseInput
{
    public string TicketPlanKindId { get; init; } = string.Empty; // e.g. T_002
    public DateOnly ActivationDate { get; init; }
    public PaymentState PaymentStatus { get; init; } // Paid / UnPaid
}

public enum PaymentState
{
    Paid = 1,
    UnPaid = 2
}
```

## 4. Use Case 介面草稿

```csharp
public interface IRegisterMembersUseCase
{
    Task<RegisterMembersResult> HandleAsync(RegisterMembersCommand command, CancellationToken ct);
}
```

```csharp
public sealed class RegisterMembersResult
{
    public IReadOnlyList<string> MemberIds { get; init; } = Array.Empty<string>();
    public string? OrderId { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? ActualAmount { get; init; }
}
```

## 5. Repository / Port 草稿

```csharp
public interface IMemberRepository
{
    Task<bool> AnyPhoneExistsAsync(IReadOnlyList<string> phones, CancellationToken ct);
    Task AddRangeAsync(IReadOnlyList<Member> members, CancellationToken ct);
}

public interface IStudentProfileRepository
{
    Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct);
    Task UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct);
}

public interface ITicketPlanRepository
{
    Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct);
}

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
}

public interface ITicketPassRepository
{
    Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
```

## 6. Use Case 流程草稿 (Pseudo)

```csharp
public async Task<RegisterMembersResult> HandleAsync(RegisterMembersCommand command, CancellationToken ct)
{
    Validate(command);

    var phones = command.Members.Select(x => x.Phone).ToList();
    EnsureNoDuplicateInRequest(phones);

    await _uow.BeginAsync(ct);
    try
    {
        // 防 race condition：交易內再查一次
        if (await _memberRepository.AnyPhoneExistsAsync(phones, ct))
            throw new DomainException("手機號碼已被註冊");

        var members = command.Members.Select(x => Member.Register(_id.NewMemberId(), x.Name, x.Phone)).ToList();
        await _memberRepository.AddRangeAsync(members, ct);

        var profiles = members.Select(StudentProfile.CreateEmpty).ToList();
        await _studentProfileRepository.AddRangeAsync(profiles, ct);

        Order? order = null;
        if (command.TicketPurchase is not null)
        {
            var plan = await _ticketPlanRepository.GetActiveByIdAsync(command.TicketPurchase.TicketPlanKindId, ct)
                ?? throw new DomainException("票券方案不存在或未上架");

            var pricing = _pricingPolicy.Calculate(plan.Price, members.Count); // >=2 打95折

            order = Order.Create(
                orderId: _id.NewOrderId(),
                buyerId: members[0].Id,
                totalAmount: pricing.TotalAmount,
                actualAmount: pricing.ActualAmount,
                paymentState: ConvertToOverallPaymentState(command.TicketPurchase.PaymentStatus),
                operatorId: command.OperatorId,
                buyAt: _clock.Now()
            );

            foreach (var member in members)
            {
                var item = OrderItem.CreateTicketItem(...);
                order.AddItem(item);

                var pass = TicketPass.Issue(
                    passId: _id.NewPassId(),
                    ownerId: member.Id,
                    orderId: order.Id,
                    orderItemId: item.Id,
                    plan: plan,
                    activationDate: command.TicketPurchase.ActivationDate,
                    paymentState: command.TicketPurchase.PaymentStatus,
                    now: _clock.Today()
                );

                await _ticketPassRepository.AddRangeAsync(new[] { pass }, ct);
                await _studentProfileRepository.UpdateCurrentTicketAsync(member.Id, CurrentTicketSnapshot.From(pass), ct);
            }

            await _orderRepository.AddAsync(order, ct);
        }

        await _uow.CommitAsync(ct);

        return new RegisterMembersResult
        {
            MemberIds = members.Select(x => x.Id).ToList(),
            OrderId = order?.Id,
            TotalAmount = order?.TotalAmount,
            ActualAmount = order?.ActualAmount
        };
    }
    catch
    {
        await _uow.RollbackAsync(ct);
        throw;
    }
}
```

## 7. Transaction 寫入順序建議

1. `users` (批次新增 members)
2. `sdt_profile` (每個 member 建立空殼)
3. 若有購票:
4. `orders` (一筆)
5. `order_items` (每個 member 一筆 ticket item)
6. `sdt_ticket_pass` (每個 member 一筆 pass)
7. 更新 `sdt_profile` 當前票券快取欄位
8. Commit

## 8. 重要防線
- `users.usr_phone` 建立 unique index，應用層再加一次查詢，雙保險。
- 金額不可由前端傳入，必須由後端以 `ticket_plan_kind` 計算。
- `ticket_plan_kind_is_active = true` 才能購買。
- `default_expire_days = -1` 的方案，`valid_edate` 使用 `null` 或系統規定最大值。
- `activationDate` 若大於 today，`valid_status = UnActive`；否則 `Active`。

## 9. 測試草稿
- 單元測試:
  - 純註冊成功
  - 註冊+購票成功
  - 重複手機失敗
  - 方案下架失敗
  - 人數>=2 折扣正確
  - 到期日推算正確
- 整合測試:
  - Transaction rollback: `order_items` 寫入失敗時，`users` 不應殘留半套資料


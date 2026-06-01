# 排課功能API實作規劃

# 排課功能 API 實作規劃

## 文件目標

- 這份文件說明 `cls_scdle_rules`、`cls_scdle_arnge`、`cls_scdle_arnge_log` 三張表對應的 API 要怎麼實作。
- 側重「可落地」：每支 API 會列出 Controller、UseCase、Repository/SQL、交易與驗證重點。

## 對應資料表

- `cls_scdle_rules`：週期模板規則（每週幾、幾點、時長、緩衝、老師）
- `cls_scdle_arnge`：實際某一天課程實例（來源 Auto/Manual）
- `cls_scdle_arnge_log`：課程異動記錄（JSON 變更欄位 + 操作者 + 備註）

## API 路由總覽（v1）

- Rules
1. **`[GET] /api/v1/schedule-rules`**
2. **`[POST] /api/v1/schedule-rules`**
3. **`[POST] /api/v1/schedule-rules/{ruleSn}`**
4. `[DELETE] /api/v1/schedule-rules/{ruleSn}`
5. `[POST] /api/v1/schedule-rules/generate-sessions`
- Sessions
1. `[GET] /api/v1/schedule-sessions`
2. `[GET] /api/v1/schedule-sessions/{arrangeSn}`
3. `[POST] /api/v1/schedule-sessions`
4. `[POST] /api/v1/schedule-sessions/{arrangeSn}`
5. `[POST] /api/v1/schedule-sessions/{arrangeSn}/cancel`
6. `[POST] /api/v1/schedule-sessions/{arrangeSn}/status`
- Logs
1. `[GET] /api/v1/schedule-sessions/{arrangeSn}/logs`
2. `[GET] /api/v1/schedule-logs`

## 命名與分層建議

- Controller
    - `ScheduleRulesController`
    - `ScheduleSessionsController`
    - `ScheduleLogsController`
- Application Namespace
    - `gym_system.Application.ScheduleRulesUseCase`
    - `gym_system.Application.ScheduleSessionsUseCase`
    - `gym_system.Application.ScheduleLogsUseCase`
- Infra Query/Repo
    - `IScheduleRepository`
    - `SqlScheduleRepository`

## Entity 拆分建議

- 建議拆成 3 個 Entity，分別對應資料表職責，避免把模板、實例、稽核混在同一個模型。
1. `ScheduleRule`（對應 `cls_scdle_rules`）
2. `ScheduleSession`（對應 `cls_scdle_arnge`）
3. `ScheduleSessionLog`（對應 `cls_scdle_arnge_log`）

### `ScheduleRule` 建議欄位與方法

- 欄位
    - `RuleSn, ClassId, DayOfWeek, StartTime, DurationMin, BufferMin, InstructorId, IsActive`
- 方法（最小可行）
    - `Create(...)`：建立規則，驗證 dayOfWeek/duration/buffer。
    - `Update(...)`：修改規則欄位。
    - `Deactivate()`：軟刪除（`IsActive = false`）。
    - `GetEndTime()`：由 `StartTime + DurationMin` 計算結束時間。

### `ScheduleSession` 建議欄位與方法

- 欄位
    - `ArrangeSn, ArrangeId, Date, ClassId, ClassNameSnapshot, ClassColorSnapshot`
    - `InstructorId, InstructorNameSnapshot`
    - `StartAt, EndAt, Status, Source, RuleSn`
- 方法（最小可行）
    - `CreateAutoFromRule(...)`：由規則展開建立 `Auto` 課程。
    - `CreateManual(...)`：手動加開建立 `Manual` 課程。
    - `Reschedule(startAt, endAt)`：調整上課時間。
    - `ChangeInstructor(instructorId, instructorName)`：調整授課老師。
    - `ChangeStatus(status)`：更新狀態並檢查狀態遷移是否合法。
    - `Cancel()`：狀態改為 `Cancel`。

### `ScheduleSessionLog` 建議欄位與方法

- 欄位
    - `LogSn, ArrangeSn, ChangedDataJson, OperatorId, LogDt, Remark`
- 方法（最小可行）
    - `Create(arrangeSn, changes, operatorId, remark)`：產生 log，並確保 `changes` 可序列化為合法 JSON。

### 先做與後做

- 先做（本期必要）
    - 上述 3 個 Entity 與最小方法，讓 API 能正確寫入/查詢三張表。
- 後做（可迭代）
    - 再抽 Value Object：`TimeRange`、`SessionStatus`、`ScheduleSource` 等。
    - 再加 Domain Event：例如規則停用時通知排程服務。

## 共用驗證規則

- `dayOfWeek` 僅允許 `0~6`。
- `status` 僅允許 `Open|Ongoing|Finished|Cancel`。
- `source` 僅允許 `Auto|Manual`。
- `startAt < endAt`。
- `durationMin > 0`，`bufferMin >= 0`。
- `class_id` 必須存在於 `class` 表。
- `instructor_id` 必須存在於老師角色（`user_role`）。
- 衝堂檢查（至少）
    - 同老師在同時段不可重疊。
    - 規則展開時，衝堂判斷要把 `bufferMin` 計入（`end + buffer`）。

## API 實作細節

### 1) `[GET] /api/v1/schedule-rules`

- 目的
    - 取得排課模板規則列表。
- Query
    - `isActive` (nullable)
    - `dayOfWeek` (nullable)
    - `instructorId` (nullable)
- Controller
    - 呼叫 `GetScheduleRulesHandler.Handle(query, ct)`。
- Application
    - 建立 `GetScheduleRulesQuery`。
    - Handler 只做參數清理與轉換。
- Infrastructure SQL
    - `SELECT ... FROM cls_scdle_rules WHERE 1=1`，依 query 動態加條件。
- 回傳欄位
    - `ruleSn, classId, dayOfWeek, startTime, durationMin, endTime, bufferMin, instructorId, isActive`。

### 2) `[POST] /api/v1/schedule-rules`

- 目的
    - 新增一筆週期排課規則。
- Request 範例

```json
{
  "classId": "CLS000001",
  "dayOfWeek": 1,
  "startTime": "09:00:00",
  "durationMin": 60,
  "bufferMin": 15,
  "instructorId": "U0000000001"
}
```

- Controller
    - 建立 `CreateScheduleRuleCommand`。
- Application
    - 驗證欄位。
    - 呼叫 repository 做衝堂預檢（同老師、同星期幾、同時段含 buffer）。
- Infrastructure SQL
    - `INSERT INTO cls_scdle_rules (...) VALUES (...)`。
    - 回傳 `SCOPE_IDENTITY()`。
- 失敗情境
    - 課程不存在、老師不存在、衝堂、欄位格式錯誤。

### 3) `[DELETE] /api/v1/schedule-rules/{ruleSn}`

- 目的
    - 實際刪除（Hard Delete）規則。
- Application 流程
    - 檢查該規則是否存在。
    - 檢查是否有「未來」已展開的課程實例關聯到此 `ruleSn`（視業務邏輯決定是否允許刪除）。
- Infrastructure SQL
    - 建議作法 A（保留歷史，切斷關聯）：
    先執行 `UPDATE cls_scdle_arnge SET ruleSn = NULL WHERE ruleSn = @ruleSn;`
    再執行 `DELETE FROM cls_scdle_rules WHERE cls_scdle_rules_sn=@ruleSn;`
    - 建議作法 B（嚴格阻擋）：
    若查到該規則已被 `cls_scdle_arnge` 使用，回傳 `409 Conflict` 錯誤，提示使用者必須先取消關聯的課程才能刪除模板。

### 4) `[POST] /api/v1/schedule-rules/{ruleSn}/deactivate`

- 目的
    - Soft delete 規則。
- SQL
    - `UPDATE cls_scdle_rules SET cls_scdle_rules_is_active=0 WHERE cls_scdle_rules_sn=@ruleSn`。
- 注意
    - 不回頭刪既有 session；規則停用只影響未來生成。

### 5) `[POST] /api/v1/schedule-rules/generate-sessions`

- 目的
    - 依啟用規則，批次展開日期區間的課程實例（Auto）。
- Request

```json
{
  "fromDate": "2026-06-01",
  "toDate": "2026-06-30",
  "overwrite": false
}
```

- Application 流程
1. 取 `is_active=1` 的 rules。
2. 在日期區間找符合星期幾的日期。
3. 依 rule 組出 `startAt/endAt`。
4. 先查是否已存在同 `ruleSn + date` 的 session。
5. 不存在才 insert，`source='Auto'`。
- Infrastructure SQL
    - 建議用 transaction 包住批次。
    - 避免重複可用「先查再寫」或建立唯一索引（建議新增唯一索引）。
- 回傳
    - `createdCount, skippedCount, conflictCount`。

### 6) `[GET] /api/v1/schedule-sessions`

- 目的
    - 查詢實際課表（週/月行事曆）。
- Query
    - `fromDate, toDate, status, instructorId, classId, source`。
- SQL
    - 主查 `cls_scdle_arnge`，依條件過濾、`ORDER BY cls_scdle_arnge_st`。
- 回傳欄位
    - `arrangeSn, arrangeId, date, classId, className, color, instructorId, instructorName, startAt, endAt, status, source, ruleSn`。

### 7) `[GET] /api/v1/schedule-sessions/{arrangeSn}`

- 目的
    - 取得單堂課詳細資訊。
- SQL
    - `SELECT TOP 1 ... FROM cls_scdle_arnge WHERE cls_scdle_arnge_sn=@arrangeSn`。

### 8) `[POST] /api/v1/schedule-sessions`

- 目的
    - 手動加開課程（Manual）。
- Request 範例

```json
{
  "date": "2026-06-15",
  "classId": "CLS000007",
  "instructorId": "U0000000006",
  "startAt": "2026-06-15T14:00:00",
  "endAt": "2026-06-15T14:30:00",
  "remark": "活動加開"
}
```

- Application
    - 驗證 class/instructor。
    - 查衝堂。
    - 從 class、users 撈快照資料（`class_name`、`class_label_color`、`instructor_name`）。
- Infrastructure SQL
    - Insert 到 `cls_scdle_arnge`，固定 `source='Manual'`，`ruleSn=NULL`。

### 9) `[POST] /api/v1/schedule-sessions/{arrangeSn}`

- 目的
    - 修改單堂課（時間、老師、狀態）。
- 交易要求
    - 必須同交易寫入 `cls_scdle_arnge` + `cls_scdle_arnge_log`。
- Application 流程
1. 撈舊資料。
2. 套新值並驗證衝堂。
3. 算變更差異 JSON（只記錄有改的欄位）。
4. Transaction：更新 session、插入 log。
- Log JSON 範例

```json
{
  "cls_scdle_arnge_instructor_id": { "old": "U0000000002", "new": "U0000000006" },
  "cls_scdle_arnge_st": { "old": "2026-05-20T18:30:00", "new": "2026-05-20T19:00:00" }
}
```

### 10) `[POST] /api/v1/schedule-sessions/{arrangeSn}/cancel`

- 目的
    - 取消課程（狀態改 `Cancel`）。
- 實作
    - 重用 Update handler 的狀態更新邏輯。
    - 寫入 log（old: Open/new: Cancel）與 remark。

### 11) `[POST] /api/v1/schedule-sessions/{arrangeSn}/status`

- 目的
    - 單獨切換狀態（Open/Ongoing/Finished/Cancel）。
- 驗證
    - 狀態遷移是否合法（建議）
        - `Open -> Ongoing -> Finished`
        - `Open|Ongoing -> Cancel`
        - `Finished` 不可改回 `Open`。
- 交易
    - 更新 session + log。

### 12) `[GET] /api/v1/schedule-sessions/{arrangeSn}/logs`

- 目的
    - 取得該堂課完整異動歷程。
- SQL
    - `SELECT ... FROM cls_scdle_arnge_log WHERE cls_scdle_arnge_sn=@arrangeSn ORDER BY cls_scdle_arnge_log_dt DESC`。

### 13) `[GET] /api/v1/schedule-logs`

- 目的
    - 後台稽核查詢。
- Query
    - `operatorId, fromDate, toDate, arrangeSn`。

## 需要新增的程式檔（建議）

- Api
    - `src/gym-system.Api/Controllers/ScheduleRulesController.cs`
    - `src/gym-system.Api/Controllers/ScheduleSessionsController.cs`
    - `src/gym-system.Api/Controllers/ScheduleLogsController.cs`
    - `src/gym-system.Api/Contracts/Schedules/...`
- Application
    - `src/gym-system.Application/ScheduleRulesUseCase/Commands/...`
    - `src/gym-system.Application/ScheduleRulesUseCase/Queries/...`
    - `src/gym-system.Application/ScheduleSessionsUseCase/Commands/...`
    - `src/gym-system.Application/ScheduleSessionsUseCase/Queries/...`
    - `src/gym-system.Application/ScheduleLogsUseCase/Queries/...`
- Domain
    - `src/gym-system.Domain/Entities/Schedules/...`
    - `src/gym-system.Domain/Repositories/IScheduleRepository.cs`
- Infrastructures
    - `src/gym-system.Infrastructures/SqlScheduleRepository.cs`

## SQL 索引建議

- `cls_scdle_arnge`
    - index on `(cls_scdle_date, cls_scdle_status)`。
    - index on `(cls_scdle_arnge_instructor_id, cls_scdle_arnge_st, cls_scdle_arnge_et)`。
    - index on `(cls_scdle_rules_sn, cls_scdle_date)` 供 Auto 展開去重。
- `cls_scdle_arnge_log`
    - index on `(cls_scdle_arnge_sn, cls_scdle_arnge_log_dt DESC)`。

## 錯誤碼建議

- `400 BadRequest`：欄位格式/必填錯誤、非法狀態。
- `404 NotFound`：rule/session 不存在。
- `409 Conflict`：衝堂、重複產生 session。
- `500`：未預期錯誤。

## 實作順序建議

1. 先做 Query API（rules list、sessions list/detail、logs list）。
2. 做 rules CRUD + deactivate。
3. 做 manual session create/update/cancel/status + log transaction。
4. 最後做 generate-sessions 批次展開。
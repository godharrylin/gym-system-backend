# Ticket Plan Rule 實作規劃

## 1. 目標與範圍

本規劃目標：
- 讓註冊頁面在載入時，從後端取得可顯示票券方案。
- 票券顯示規則由資料庫管理，不寫死在前端。
- 前端 `tags`（或後續改名 `rules`）可由 API 回傳。

本次只處理：
- 票券方案清單讀取。
- 規則資料模型設計。
- 查詢 API 契約。

不處理：
- 真正購買時的資格驗證（例如新客限定是否已購買過）。

## 2. 現況對齊

你目前已有：
- `ticket_plan_kind`：票券方案主檔（名稱、價格、預設天數、預設點數、是否上架）。
- 前端使用 `MOCK_PRODUCTS`，其中 `tags` 用於畫面顯示判斷。

問題：
- `tags` 尚未落在 DB，前後端規則來源不一致。

## 3. 命名建議

建議採用語意明確命名：
- `plan_rule`：規則字典表（可跨票券/商品共用）。
- `ticket_plan_kind_rule`：票券方案種類規則關聯表（多對多）。

中文名稱：
- `plan_rule`：規則字典表
- `ticket_plan_kind_rule`：票券方案種類規則關聯表

## 4. 資料表設計

### 4.1 `plan_rule`（規則字典）

用途：定義有哪些規則可被套用。

欄位建議：
- `plan_rule_sn`：PK，`varChar`，格式可用 `R_001`
- `plan_rule_code`：唯一索引，`varChar`，例如 `NEW_ONLY`
- `plan_rule_name`：`varChar`，例如「新客限定」
- `plan_rule_desc`：`Text`（nullable）
- `plan_rule_is_active`：`boolean`（全域開關，通常保留）
- `plan_rule_create_dt`：`DateTime`
- `plan_rule_up_dt`：`DateTime`

### 4.2 `ticket_plan_kind_rule`（多對多關聯）

用途：紀錄某個 `ticket_plan_kind` 套用了哪些規則。

欄位建議：
- `ticket_plan_kind_sn`：FK -> `ticket_plan_kind.ticket_plan_kind_sn`
- `plan_rule_sn`：FK -> `plan_rule.plan_rule_sn`
- `ticket_plan_kind_rule_is_active`：`boolean`（主控某方案是否套用該規則）
- `ticket_plan_kind_rule_create_dt`：`DateTime`
- `ticket_plan_kind_rule_up_dt`：`DateTime`

鍵值建議：
- 複合主鍵：`(ticket_plan_kind_sn, plan_rule_sn)`
- 索引：`plan_rule_sn`（便於反查）

啟用欄位語意建議：
- `plan_rule_is_active`：控制規則字典是否仍可被全系統使用。
- `ticket_plan_kind_rule_is_active`：控制某個票券方案是否啟用該規則（此欄位是你目前場景主判斷）。

## 5. 初始規則資料（Seed）

建議先建立以下 rule code：
- `FAMILY_ELIGIBLE`
- `RENEWAL`
- `NEW_ONLY`
- `HIDDEN`

## 6. 方案與規則對照（依目前前端需求）

對照建議：
- `single` -> 無規則
- `monthly` -> `FAMILY_ELIGIBLE`
- `renew` -> `RENEWAL`, `FAMILY_ELIGIBLE`
- `new_promo` -> `NEW_ONLY`
- `free_trial` -> `NEW_ONLY`
- `pack_10` -> `FAMILY_ELIGIBLE`
- `pack_20` -> `FAMILY_ELIGIBLE`
- `coupon`（或 custom） -> `HIDDEN`

注意：
- `monthly` 若尚未列在 `ticket_plan_kind_code` enum，請補上。

## 7. API 契約（先維持前端相容）

建議端點：
- `GET /api/v1/ticket-plan-kinds`

查詢條件（可選）：
- `activeOnly=true`（預設 true）
- `includeHidden=false`（預設 false）

回傳格式（先相容前端 `tags`）：

```json
[
  {
    "id": "p_monthly",
    "code": "monthly",
    "name": "月票 (Monthly)",
    "price": 1960,
    "days": 30,
    "sessions": "UNLIMITED",
    "type": "MONTHLY",
    "tags": ["FAMILY_ELIGIBLE"],
    "description": "30天內不限次數"
  }
]
```

轉換規則：
- `ticket_plan_kind_default_credit = 999` 時，回傳 `sessions = "UNLIMITED"`。
- 否則回傳數字。

## 8. 後端分層放置建議（照你現有專案）

### 8.1 Domain

使用既有 `TicketPlanKind` 作為方案主實體即可，不需再新增同義 `TicketPlan`。

新增或調整：
- `TicketPlanRule` 實體
- `TicketPlanKindRule` 關聯實體（或在 ORM mapping 層處理）

### 8.2 Application

建立查詢 Use Case：
- `Features/TicketPlanKinds/Queries/GetTicketPlanKinds/...`

內容：
- Query
- Handler
- Response DTO（包含 `tags`）
- Repository Interface（查詢帶出規則）

### 8.3 Infrastructures

實作 Repository：
- 透過 Join 取 `ticket_plan_kind` + `ticket_plan_kind_rule` + `plan_rule`
- 在此層組裝 `tags` 陣列

### 8.4 Api

新增 Controller Endpoint：
- `TicketPlanKindsController` 的 `GET`
- 僅負責接參數與回傳結果

## 9. 前端串接步驟

1. 把 `MOCK_PRODUCTS` 改為 API 來源。
2. 頁面載入時呼叫 `GET /api/v1/ticket-plan-kinds`。
3. `tags` 繼續沿用既有畫面判斷邏輯。
4. 加入三種狀態：loading / error / empty。
5. `HIDDEN` 規則先由前端隱藏；未來可改為後端 `includeHidden=false` 直接過濾。

## 10. 驗收清單

- DB 有兩張新表，且外鍵正確。
- 至少 4 個 rule code seed 完成。
- API 可回傳每個方案對應 `tags`。
- 前端註冊頁重整後，可顯示與 `MOCK_PRODUCTS` 一致的方案。
- 前端不再硬編碼 code-to-tags 對照。

## 11. 第二階段（建議）

第二階段再做：
- 把「新客限定、續約條件」從顯示規則升級成交易時後端驗證規則。
- 前端欄位名稱逐步由 `tags` 過渡到 `rules`（可先雙欄位相容）。




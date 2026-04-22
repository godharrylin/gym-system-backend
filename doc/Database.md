# Database New

- 人員資料表 **`users`**
    - 所有角色共用的基本資料。
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `usr_id` | Primary Key | 唯一ID | C00001 |
    | `usr_active` | bool | 帳號 啟用/停用 | • 啟用: 1
    • 停用: 0 |
    | `usr_pwd`  | varChar | 使用者密碼
    唯一，目前都是用手機號碼
    admin →0900000000 |  |
    | `usr_name` | Text | 學生姓名 | 王小明 |
    | `usr_phone` | Text | 聯絡電話 | 0912345678 |
    | `usr_create_dt` | DateTime | 建立時間 | 2025-11-01 |
- 角色表 **`bmc_role`**
    - Create Table Code
        
        ```sql
        -- bmc_role
        CREATE TABLE dbo.bmc_role (
            bmc_role_id         INT            IDENTITY(1,1) NOT NULL,
            bmc_role_name       NVARCHAR(50)   NOT NULL,
            bmc_role_code       VARCHAR(30)    NOT NULL,
            bmc_role_cdt  DATETIME2(0)   NOT NULL CONSTRAINT DF_bmc_role_create_dt DEFAULT (SYSDATETIME()),
            bmc_role_upd_dt     DATETIME2(0)   NOT NULL CONSTRAINT DF_bmc_role_upd_dt DEFAULT (SYSDATETIME()),
            CONSTRAINT PK_bmc_role PRIMARY KEY (bmc_role_id),
            CONSTRAINT UQ_bmc_role_code UNIQUE (bmc_role_code),
        );
        
        INSERT INTO dbo.bmc_role (bmc_role_name, bmc_role_code)
        VALUES 
        (N'員工', 'Staff'),
        (N'老師', 'Instructor'),
        (N'學生', 'Student'),
        (N'管理者', 'Admin');
        ```
        
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`bmc_role_id`** | Primary Key | 唯一ID |  |
    | **`bmc_role_name`** | nvarChar |   • 員工
      • 老師
      • 學生
      • 管理者 |  |
    | **`bmc_role_code`** | nvarChar |   • 員工→ `Staff`
      • 老師→ `Instructor`
      • 學生→ `Student`
      • 管理者→ `Admin` |  |
    | **`bmc_role_cdt`** | DateTime | 建立日期 |  |
    | **`bmc_role_upd_dt`** | DateTime | 更新日期 |  |
- 人員角色關聯資料表 `user_role`
    - 一個人可以有多個角色
    - **`usr_id`** 、**`bmc_role_id`** 當作複合主鍵
    - Create Table code
        
        ```sql
        CREATE TABLE dbo.user_role (
            usr_id               NVARCHAR(50)  NOT NULL,
            bmc_role_id          INT           NOT NULL,
            user_role_is_active  NVARCHAR(2)   NOT NULL CONSTRAINT DF_user_role_is_active DEFAULT (1),
            user_role_cdt        DATETIME2(0)  NOT NULL CONSTRAINT DF_user_role_cdt DEFAULT (SYSDATETIME()),
            user_role_upd_dt      DATETIME2(0)  NOT NULL CONSTRAINT DF_user_role_upd_dt DEFAULT (SYSDATETIME()),
            CONSTRAINT PK_user_role PRIMARY KEY (usr_id, bmc_role_id),
        );
        
        INSERT INTO user_role(usr_id, bmc_role_id)
        VALUES 
        (N'admin_01', 4),
        (N'admin_01', 2),
        (N'U00002', 2),
        (N'U00003', 2),
        (N'U00006', 2);
        ```
        
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`usr_id`** | Primary Key | 人員id，必須來自`users.usr_id` |  |
    | **`bmc_role_id`** | Primary Key | 角色id，必須來自**`bmc_role.bmc_role_id`** |  |
    | **`user_role_is_active`** | bool | 身分別是否啟用 |  |
    | **`user_role_cdt`** | DateTime | 建立日期 |  |
    | **`user_role_upd_dt`** | DateTime | 更新日期 |  |
- 學生擴展表 `sdt_profile`
    - 目前這張表當作快取中心，放的內容是最近一次進場時間及最新的票券，包含剩餘堂數。
    - 未來可以擴充緊急連絡人等靜態欄位資訊
    - 該表更新時機
        - **進場成功時：** 更新 `last_visit_at`。
        - **扣點/核銷時：** 更新 `plan_balance_display` (例如從 10 left 變 9 left) 和 `valid_state`。
        - **購買或付清時：** 更新 `payment_state`。
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `usr_id` | Primary Key | 和User  資料表的`usr_id` 一致 | C00001 |
    | `sdt_profile_cur_visit_at` | DateTime | 最近一次進場時間
    以三叉機log資料更新 |  |
    | `sdt_cur_ticket_id`  | varChar | 最新的票券id |  |
    | `sdt_cur_ticket_type` | Enum | 最新一筆票券的種類 |  |
    | `sdt_cur_ticket_valid_state` | Enum | 最新一筆票券的啟用狀態 |  |
    | `sdt_cur_ticket_payment_state` | Enum | 最新一筆票券的付款狀態 |  |
    | `sdt_cur_ticket_remain_count` | int(nullable) | 最新一筆票券的剩餘堂數(堂票才會有) |  |
    | `sdt_cur_ticket_expire_dt` | DateTime | 最新一筆票券的到期日 |  |
    | `sdt_cur_ticket_up_dt` | DateTime | 更新時間戳 |  |
- 學生票券資料表 `sdt_ticket_pass`
    - 學生持有的票券資訊，包括、到期日、使用次數、付款狀態，等使用權利
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `pass_id` | Primary Key | 學生票券流水號 |  |
    | `order_items_sn` | Foreign Key | 關聯到訂單明細表 |  |
    | `orders_sn` | Foreign Key | 關聯到訂單表(主表) |  |
    | `owner_id` | Foreign Key | 和`users.usr_id`對應 | C00001 |
    | `valid_status` | Text | 啟用狀態:
    `UnActive`(未啟用)
    `Active`(啟用中)
    `Expire`(已過期)
    `Depleted` (已用完)
     | `Active` |
    | `valid_sdate` | DateTime | **生效日期** |  |
    | `valid_edate` | DateTime | **票券到期日** |  |
    | 堂票(Pack) 專用欄位 |  |  |  |
    | `credits_total` | int | 購買總堂數 | 10 |
    | `credits_remaing` | int | 剩餘堂數 | 9 |
- 學生出席課程紀錄表 `sdt_att_record`
    - 資料新增條件: 從三叉機傳送進入的紀錄過10min 後都沒有出去的紀錄。
    
    | **欄位名稱** | **資料類型** | **說明** |
    | --- | --- | --- |
    | **`sdt_att_record_sn`** | int | 出席流水號 |
    | **`user_id`** | Foreign Key | 使用者編號，關連到`user`表，用它來驗證身份。 |
    | **`cls_scdle_arnge_sn`** | Foreign Key | 課程編號，關連到`cls_scdle_arnge`表 |
    | **`sdt_att_record_ticket_id`** | Foreign Key | 票券，以什麼票券進入該堂課 |
    | **`sdt_att_record_status`** | varChar | 出席狀態
    • `Pending`(暫時進出)，進場後，10min內出去會是這個狀態。
    • `CheckIn`(已簽到)，進場10min後都在內場，會變這個狀態。
    • `CheckOut`(已簽退)，課程結束後，有簽退會是這個狀態。 |
    | **`sdt_att_record_chk_in_time`** | DateTime | 簽到 |
    | **`sdt_att_record_chk_out_time`** | DateTime | 簽退 |
    | **`sdt_att_record_remark`** | varChar (Nullable) | 備註 |
    | **`sdt_att_record_upd_source`** | varChar | 異動來源
    • `Costumer_Click` (客人點擊)
    • `System_ForgotCheckout` (系統更新)
    • `Staff_Manual` (員工手動補登) |
    | **`sdt_att_record_upd_by_staff`** | varChar | 如果是員工補登，此欄位會顯示員工的id |
    | **`sdt_att_record_upd_date`** | DateTime | 資料異動時間 |
    
- 票券核銷表 `sdt_ticket_usage_log`
    - 學生票券的使用紀錄，Insert only
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `usage_sn` | Primary Key | 核銷紀錄流水號 | 1, 2, 3... |
    | `pass_id` | Foreign Key | 關聯到 `sdt_ticket_pass` | P001 |
    | `usage_dt` | DateTime | 核銷/進場時間 | 2026-03-13 19:30:00 |
    | `usage_type` | Enum | 核銷方式：
     • `Manual` (櫃檯手動)
     • `Gate_Entry` (閘門進場)
     • **`Auto_Consecutive`** (未離場，自動核銷) | Gate |
    | `deducted_credits` | Int | 這次扣了多少堂（月票則存 0） | 1 |
    | `rem_credits_snapshot` | Int | (保命欄位) 核銷後的剩餘堂數快照 | 9 |
    | `operator_id` | VarChar | 操作者 ID（閘門進場可存 System） | admin_01 |
    | `remark` | Text | 備註 | 逾時未進場補發、或手動扣點說明 |
- 訂單表 `orders`
    
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `orders_sn` | Primary Key | 訂單流水號 | C0001 |
    | `orders_buyer` | varChar | 購買者id | `users.usr_id` |
    | `orders_overall_payment_state` | enum | 訂單總付款狀態
      • `Paid` (全品項付清)
      • `PartialPaid` (部分品項付清)
      • `UnPaid` (未付清)
      • `Cancel` (取消訂單) |  |
    | `orders_total_amount`  | decimal | 訂單總金額 |  |
    | `orders_actual_amount`  | decimal | 訂單實收總額 |  |
    | `order_buy_date` | DateaTime | 購買日期 |  |
    | `orders_create_pn` | varChar | 建立訂單的人(當時操作系統的使用者) | `users.usr_id` |
    | `orders_create_dt` | DateTime | 建立該筆訂單的時間 |  |
    | `orders_up_pn` | varChar | 更新該筆訂單的人 | `users.usr_id` |
    | `orders_up_dt` | DateTime | 更新該筆訂單的時間 |  |
- 訂單明細表 `order_items`
    
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `order_items_sn` | Primary Key | 訂單品項流水號 |  |
    | `orders_sn` | Foriegn Key | 訂單流水號 |  |
    | `order_items_type` | Enum | 訂單品項類別
      • `Ticket` 
      • `Product`  |  |
    | `order_items_ref_id` | varChar | 訂單種類編號
      • 票券→`ticket_plan_kind_sn`     
      • 商品 → `product_id` |  |
    | `order_items_payment_state` | Enum | 付款狀態
      • `Paid`
      • `UnPaid`
      • `Cancel` |  |
    | `order_items_payment_method` | Enum | 付款方式
      • 目前會是Cash |  |
    | `order_items_total_amount` | Decimal | 這筆訂單應收的錢 |  |
    | `order_items_actual_amount` | Decimal | 這筆訂單實際收的錢 |  |
    | `order_items_quantity` | int | **品項購買數量**
    票券
     • 月票 → 單位是天
     • 堂票 → 單位是堂
    商品 → 單位是個 |  |
    | `order_items_bonus_quantity` | int | **訂單品項贈送數量**
    票券
     • 月票 → 單位是天
     • 堂票 → 單位是堂
    商品 → 單位是個 |  |
    | `order_items_buy_date` | DateaTime | 購買日期 |  |
    | `order_items_create_dt` | DateTime | 建立票券檔案的時間 |  |
    | `order_items_up_pn` | varChar | 更新檔案的人 | `users.usr_id` |
    | `order_items_up_dt` | DateTime | 更新檔案時間 |  |
- 訂單通用變更紀錄表 `order_audit_logs`
    - 利用`batch_id` 「把同一次操作中，產生的多筆欄位改動串在一起」。
        
        當使用者做一次**「付錢」(Pay)**的操作時，系統會改動**多個資料表 / 多個欄位**，因此會產生**多筆 audit log**。這些 log 雖然是不同資料，但都屬於**同一個行為**，所以用**同一個 `batch_id`**來關聯。
        
    - 另一個比較省空間的改動是，以整張表為單位改。
        - target_table 欄位
        - change_data欄位(JSON格式)
    
    | **欄位名稱** | **資料型別** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`log_sn`** | Primary Key | 紀錄流水號 | 1, 2, 3... |
    | **`orders_sn`** | **FK (索引)** | 整筆交易的編號（必填，用來快速找出一筆單的所有歷史）。 |  |
    | **`batch_id`** | varChar | 該次改動的流水號
    (有可能一次改兩個欄位，所以該欄位可以重複) | C20260313001 |
    | **`order_items_sn`** | Foreign Key | **(可為空)** 關聯的明細流水號 | ITM_005 (若是改主表則填 NULL) |
    | **`target_table`** | Varchar | 發生變動的資料表名稱 | `orders` 或 `order_items` |
    | **`target_column`** | Varchar | **變動的欄位名稱** | `payment_state`, `actual_amount` |
    | **`old_value`** | **NVarChar** | 變更前的值（轉成文字儲存） | `Unpaid` 或 `1860` |
    | **`new_value`** | **NVarChar** | 變更後的值（轉成文字儲存） | `Paid` 或 `1800` |
    | **`operator_id`** | Varchar | 操作者 ID（是誰改的） | `admin_01` |
    | **`log_dt`** | DateTime | 紀錄產生的時間 | 2026-03-13 14:00:00 |
    | **`remark`** | Text | 備註（選填） | 學生現場付現、手動折扣 60 元 |
- 票券種類表 `ticket_plan_kind`
    - 負責分類票券種類
    - Create Table code
        
        ```sql
        CREATE TABLE dbo.ticket_plan_kind (
            ticket_plan_kind_sn INT IDENTITY(1,1) PRIMARY KEY,  -- 主鍵流水號
        
            ticket_plan_kind_code NVARCHAR(50) NOT NULL,        -- 代碼
            ticket_plan_kind_type NVARCHAR(20) NOT NULL,        -- 類型
            ticket_plan_kind_cname NVARCHAR(100) NOT NULL,       -- 中文名稱
        
            ticket_plan_kind_price DECIMAL(10,2) NOT NULL,      -- 價格
            ticket_plan_kind_default_credit INT NOT NULL,       -- 點數
            ticket_plan_kind_default_expire_days INT NOT NULL,  -- 有效天數
        
            ticket_plan_kind_default_is_active varchar(1) NOT NULL     -- 是否啟用
        )
        
        INSERT INTO dbo.ticket_plan_kind (
            ticket_plan_kind_code,
            ticket_plan_kind_type,
            ticket_plan_kind_cname,
            ticket_plan_kind_price,
            ticket_plan_kind_default_credit,
            ticket_plan_kind_default_expire_days,
            ticket_plan_kind_default_is_active
        )
        VALUES
        -- 單次與抵用券
        ('SINGLE',      'PACK',   '單次票',       250,   1,   1,   'Y'),
        ('COUPON',      'PACK',   '折抵票',       0,     1,   30,  'Y'),
        ('FREE_TRIAL',  'PACK',   '免費體驗票',    0,     1,   14,   'Y'),
        
        -- 堂票系列 (PACK)
        ('PACK_10',     'PACK',   '10堂票',       2300,  10,  90,  'Y'),
        ('PACK_20',     'PACK',   '20堂票',       4400,  20,  90,  'Y'),
        ('NEW_PROMO',   'PACK',   '5堂票-新朋友',  1200,  5,   30,  'Y'),
        
        -- 月票系列 (M_PASS)
        ('MONTHLY',     'M_PASS', '月票',            1960,  99999, 30,  'Y'),
        ('RENEW',       'M_PASS', '續約票',          1860,  99999, 30,  'Y'),
        ('B6G1',        'M_PASS', '半年票 買6送1',   11760, 99999, 210, 'Y'),
        ('B12G2',       'M_PASS', '年票 買12送2',    23520, 99999, 420, 'Y');
        ```
        
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `ticket_plan_kind_sn` | nVarChar(PK) | 僅提供資料庫關聯使用 |  |
    | `ticket_plan_kind_code` | Enum | 票券代碼，唯一索引，提供工程師在程式碼中調用
      • `SINGLE`(單次)
      • `COUPON`(折抵票)
      • `PACK_10`(10堂票)
      • `PACK_20`(20堂票)
      • `NEW_PROMO`(是否為新客 ，用於"5堂-新朋友"方案，這個方案只能買一次)
      • `RENEW`(續約票，在過期後10天繳費有優惠價格)
    • `MONTHLY`月票
    • `B12G2` 買12送2月票
    • `B6G1` 買6送1月票
    • `FREE_TRIAL`(免費體驗票) |  |
    | `ticket_plan_kind_type` | Enum | 票券種類
      • `PACK`(堂票) 
      • `M_PASS`(月票) |  • `PACK`(堂票)
        • `SINGLE`
        • `COUPON` 
        • `PACK_10`
        • `PACK_20`
        • `FREE_TRIAL`
        • `NEW_PROMO`
     • `M_PASS`(月票)
        • `RENEW`
        • `MONTHLY`
        • `B12G2` 
        • `B6G1`  |
    | `ticket_plan_kind_cname` | varChar | 票券名稱
      • `SINGLE`→ 單次
      • `MONTHLY`→ 月票
      • `COUPON` → 抵用券
      • `PACK_10`→ 10堂票
      • `PACK_20`→ 20堂票
      • `NEW_PROMO`→ 新朋友方案
      • `RENEW`→ 續約票
      • `FREE_TRIAL`→ 免費體驗
    票
      • `B12G2` →買12送2月票
      • `B6G1` →買6送1月票 |  |
    | `ticket_plan_kind_price` | int | 票券價格
      • `SINGLE`→ 250
      • `MONTHLY`→ 1960
      • `COUPON` → 0
      • `PACK_10`→ 2300
      • `PACK_20`→ 4400
      • `NEW_PROMO`→ 1200
      • `RENEW`→ 1860 
      • `FREE_TRIAL`→ 0
      • `B12G2` →23520
      • `B6G1` → 11760 |  |
    | `ticket_plan_kind_default_credit` | Decimal | 預設使用次數
    月票一律999 |  • `M_PASS`→ 99999
     • `SINGLE`→ 1
     • `COUPON` → 1
     • `PACK_10`→ 10
     • `PACK_20`→ 20
     • `FREE_TRIAL`→ 1
     • `NEW_PROMO`→ 5
     |
    | `ticket_plan_kind_default_expire_days` | int | 預設到期天數 |   • `SINGLE`→ 1
      • `MONTHLY`→ 30
      • `COUPON` → 30
      • `PACK_10`→ 90
      • `PACK_20`→ 90
      • `NEW_PROMO`→ 30
      • `RENEW`→ 30
      • `FREE_TRIAL`→ 14
      • `B12G2` → 420
      • `B6G1` → 210 |
    | `ticket_plan_kind_is_active` | varChar(1) | 是否上架
      • `Y`上架
      • `N`下架不顯示 |  |
- 規則定義表 `plan_rule`
    - 定義產品(票券、商品、課程)適用的規則，為了能夠讓前端畫面根據規則顯示可購買的票券。
    - Create Table code
        
        ```sql
        -- 1) Tag 字典表
        CREATE TABLE plan_rule (
            plan_rule_sn            VARCHAR(20)    NOT NULL PRIMARY KEY, -- R_001
            plan_rule_code          VARCHAR(50)    NOT NULL UNIQUE,      -- NEW_ONLY
            plan_rule_name          NVARCHAR(100)  NOT NULL,             -- 新客限定
            plan_rule_desc          NVARCHAR(255)  NULL,
            plan_rule_is_active     VARCHAR(2)     NOT NULL DEFAULT 'Y',
            plan_rule_create_dt     DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
            plan_rule_up_dt         DATETIME2      NOT NULL DEFAULT SYSDATETIME()
        );
        
        INSERT INTO plan_rule (
            plan_rule_sn,
            plan_rule_code,
            plan_rule_name,
            plan_rule_desc
        )
        VALUES
        ('R_001', 'NEW_ONLY', '新會員限定', '只能讓新會員使用'),
        ('R_002', 'RENEWAL', '續約方案', '符合續約資格可使用'),
        ('R_003', 'FAMILY_ELIGIBLE', '家庭方案', '符合家庭方案資格'),
        ('R_004', 'HIDDEN', '特殊方案', '特殊方案');
        ```
        
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `plan_rule_sn` | varChar(PK) | 僅提供資料庫關聯使用格式: R_數字`R_001` |  |
    | `plan_rule_code` | Enum(Unique) | 規則代碼，唯一索引，提供工程師在程式碼中調用 
      • `FAMILY_ELIGIBLE` 家庭方案
      • `RENEWAL` 續約方案
      • `NEW_ONLY` 新會員方案
      • `HIDDEN` 特殊方案 |  |
    | `plan_rule_name` | varChar | 規則名稱
      • `FAMILY_ELIGIBLE` →家庭方案
      • `RENEWAL`  →續約方案
      • `NEW_ONLY`  →新會員方案
      • `HIDDEN`  →特殊方案 |  |
    | `plan_rule_desc` | varChar | 規則描述
      • `FAMILY_ELIGIBLE` →95折優惠
      • `RENEWAL`  → 續約優惠
      • `NEW_ONLY`  →限定新會員使用
      • `HIDDEN`  →特殊方案 |  |
    | `plan_rule_is_active` | boolean | 規則是否啟用，全域開關
     • `true` 啟用
      • `false` 不啟用 |  |
    | `plan_rule_create_dt` | DateTime | 規則建立日期 |  |
    | `plan_rule_up_dt` | DateTime | 規則更新日期 |  |
- 票券種類規則關聯表 `ticket_plan_kind_rule`
    - 定義哪一種票券套用哪些規則。一種票券可以適用多個規則；一種規則可以用在多個票券。
    - `ticket_plan_kind_sn` , `ticket_plan_rule_sn` 當複合主鍵。
    - Create Table code
        
        ```sql
        CREATE TABLE ticket_plan_kind_rule (
            ticket_plan_kind_sn            INT          NOT NULL,
            ticket_plan_rule_sn            VARCHAR(20)  NOT NULL,
            ticket_plan_kind_tag_create_dt DATETIME2    NOT NULL DEFAULT SYSDATETIME(),
        
            CONSTRAINT PK_ticket_plan_kind_rule 
                PRIMARY KEY (ticket_plan_kind_sn, ticket_plan_rule_sn),
        
            CONSTRAINT FK_tpkt_kind 
                FOREIGN KEY (ticket_plan_kind_sn) 
                REFERENCES ticket_plan_kind(ticket_plan_kind_sn),
        
            CONSTRAINT FK_tpkt_rule  
                FOREIGN KEY (ticket_plan_rule_sn)  
                REFERENCES plan_rule(plan_rule_sn)
        );
        ```
        
    - 目前票券對應的方案表
        
        
        | **票券名稱 (Name)** | **票種 (Type)** | **目前的 Tags 設定** | **這些 Tags 產生的影響** |
        | --- | --- | --- | --- |
        | 單次票 (Single Pass) | 次數票 (SESSION) | [] (無) | 所有人皆可看見、購買。不支援家庭方案。 |
        | 月票 (Monthly) | 期限票 (MONTHLY) | ['FAMILY_ELIGIBLE'] | 可用於家庭方案折扣。 |
        | 半年票 買6送1 (7 Months) | 期限票 (MONTHLY) | ['FAMILY_ELIGIBLE'] | 可用於家庭方案折扣。 |
        | 年票 買12送2 (14 Months) | 期限票 (MONTHLY) | ['FAMILY_ELIGIBLE'] | 可用於家庭方案折扣。 |
        | 續約票 (Renewal) | 期限票 (MONTHLY) | ['RENEWAL', 'FAMILY_ELIGIBLE'] | 限舊會員續約時才會顯示。可用於家庭方案折扣。 |
        | 5堂票-新朋友 (Short Trial) | 次數票 (SESSION) | ['NEW_ONLY'] | 限新會員才會顯示。不支援家庭方案。 |
        | 免費體驗票 (Free Trial) | 次數票 (SESSION) | ['NEW_ONLY'] | 限新會員才會顯示。不支援家庭方案。 |
        | 10堂票 (10 Sessions) | 次數票 (SESSION) | ['FAMILY_ELIGIBLE'] | 可用於家庭方案折扣。 |
        | 20堂票 (20 Sessions) | 次數票 (SESSION) | ['FAMILY_ELIGIBLE'] | 可用於家庭方案折扣。 |
        | 折抵票 (Custom) | 次數票 (SESSION) | ['HIDDEN'] | （預計）在前台結帳介面中要被隱藏起來，僅供內部邏輯替換使用。 |
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | `ticket_plan_kind_sn` | varChar(PK)、FK | 票券種類，必須來自
    `ticket_plan_kind.ticket_plan_kind_sn` |  |
    | `plan_rule_sn` | VarChar(PK)、FK | 規則種類，必須來自
    `plan_rule.ticket_plan_rule_sn` |  |
    | `ticket_plan_kind_rule_create_dt`  | DateTime | 該規則建立日期 |  |
- 產品種類表 `products` (目前暫時用不到)
    
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`products_id`** | PK | 商品內部流水號 |  |
    | **`products_code`** | varChar | 商品代碼
    給`order_items_ref_id` 關聯用 |  |
    | **`products_name`** | varChar | 商品名稱 |  |
    | **`products_category`** | Enum | 商品類別
    `Food`, `Equipment` |  |
    | **`products_price`** | Decimal | 商品價格(定價) |  |
    | **`products_stock_qty`** | int  | 商品庫存量 |  |
    | **`products_safety_stock`** | int | 安全庫存量 |  |
    | **`products_is_active`** | boolean | 是否上架 |  |
    | **`products_create_dt`** | DateTime | 商品建立日 |  |
    | **`products_update_dt`** | DateTime | 最後更新時間(最後異動庫存時間) |  |
- 課程定義表 `class`
    - 紀錄課程資訊
    - Create Table Code
        
        ```sql
         CREATE TABLE dbo.class (
            -- 課程序號 (主鍵)
            class_sn            INT             IDENTITY(1,1) NOT NULL,
            -- 課程名稱
            class_name          NVARCHAR(100)   NULL,
            -- 標籤顏色
            class_label_color   NVARCHAR(20)    NULL,
            -- 課程時長
            class_duration      INT             NULL,
            -- 是否免費 (0: 否, 1: 是)
            class_is_free       NVARCHAR(2)     NULL CONSTRAINT DF_class_is_free DEFAULT ('N'),
            -- 授課老師 ID (外鍵)
            class_instructor_id NVARCHAR(50)    NULL,
            -- 課程狀態 (0: 下架, 1: 上架)
            class_is_active     NVARCHAR(2)     NULL CONSTRAINT DF_class_is_active DEFAULT ('Y'),
            -- 課程分類
            class_type          NVARCHAR(50)    NULL,
            -- 建立資訊
            class_create_pn     NVARCHAR(50)    NULL,
            class_create_dt     DATETIME        NULL CONSTRAINT DF_class_create_dt DEFAULT (GETDATE()),
            -- 更新資訊
            class_up_pn         NVARCHAR(50)    NULL,
            class_up_dt         DATETIME        NULL CONSTRAINT DF_class_up_dt DEFAULT (GETDATE()),
            -- 定義主鍵
            CONSTRAINT PK_class PRIMARY KEY (class_sn),
        );
        
        -- 請確保 dbo.users 表中已經存在這些 usr_id (例如: T001, T002...)
        INSERT INTO dbo.class (
            class_name, 
            class_label_color, 
            class_duration, 
            class_is_free, 
            class_instructor_id, 
            class_is_active, 
            class_type, 
            class_create_pn, 
            class_up_pn
        )
        VALUES 
        (N'基礎重量訓練', N'#FF5733', 60, 0, N'T001', 1, N'重訓', N'Admin', N'Admin'),
        (N'極限燃脂拳擊', N'#C70039', 50, 0, N'T002', 1, N'有氧', N'Admin', N'Admin'),
        (N'舒緩陰瑜珈', N'#DAF7A6', 90, 1, N'T001', 1, N'瑜珈', N'Admin', N'Admin'),
        (N'核心皮拉提斯', N'#581845', 60, 0, N'T003', 1, N'核心', N'Admin', N'Admin'),
        (N'進階健體專班', N'#2ECC71', 120, 0, N'T002', 0, N'重訓', N'Admin', N'Admin'); -- 這筆預設為下架狀態
        ```
        
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`class_sn`** | PK | 課程流水號 |  |
    | **`class_name`** | varChar | 課程名稱 |  |
    | **`class_label_color`** | varChar | 課程標籤顏色(對應到前端顏色) |  |
    | **`class_duration`** | int | 上課時長(min) |  |
    | **`class_is_free`** | Boolean | 該堂課是否免費 |  |
    | **`class_instructor_id`** | Foreign key | 該課程指導老師，關連到 `users` 表 | 不一定要有這個 |
    | **`class_instructor_name`** | nvarChar | 該課程老師名字，讓刪除老師資料時，不要因為完全依賴`users`表而報錯 |  |
    | **`class_is_active`** | boolean | 該堂課現在是否開課狀態 |  |
    | **`class_type`** | Enum | 課程種類
    目前沒分類，當作未來擴充 |  |
    | **`class_create_pn`** | Foreign key | 建立課程人員 |  |
    | **`class_create_dt`** | DateTime | 建立日期 |  |
    | **`class_up_pn`**  | Foreign key | 更新人員 |  |
    | **`class_up_dt`**  | DateTime | 更新日期 |  |
- 排課規則表 `cls_scdle_rules`
    - 這張資料表會作為schedule template 使用。
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`cls_scdle_rules_sn`** | Primary Key | 規則唯一編號 | 1 |
    | **`class_sn`** | Foreign Key | 關聯到 `class`表 | 10 (瑜珈課) |
    | **`cls_scdle_rules_day_wk`** | Int | 星期幾 (1-7 或 0-6) | 1 (週一) |
    | **`cls_scdle_rules_st`** | Time | 開始時間 (不含日期) | 09:00:00 |
    | **`instructor_id`** | Foreign Key | 預設老師 ID (若不填則抓 class 表的) | admin_01 |
    | **`cls_scdle_rules_is_active`** | Boolean | 此規則是否還在執行 | true |
- 排課實例表 `cls_scdle_arnge`
    - 記錄實際安排的課程
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`cls_scdle_arnge_sn`** |  | 排課流水號 |  |
    | **`class_sn`** |  | 關連到**`class`** 表 |  |
    | **`cls_scdle_arnge_st`** | DateTime | 課程開始時間 |  |
    | **`cls_scdle_arnge_et`** | DateTime | 課程結束時間 |  |
    | **`cls_scdle_arnge_instructor_id`** |  | 這堂課當前的指導老師
    關連到**`users`** |  |
    | **`cls_scdle_arnge_wk_day`** |  | 該堂課是星期幾 |  |
    | **`cls_scdle_status`** |  | 開課狀態
      • **`Cancel`**
      • **`Open`** |  |
    | **`cls_scdle_rule`** |  | 產生課程規則
      • `Auto`
      • `Manual` |  |
    | **`cls_scdle_arnge_create_dt`** |  | 建立時間 |  |
    | **`cls_scdle_arnge_up_dt`** |  | 更新時間 |  |
- 排課異動表 `cls_scdle_arnge_log`
    
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`cls_scdle_arnge_log_sn`** | Primary Key | 紀錄流水號 | 1, 2, 3... |
    | **`cls_scdle_arnge_log_sn`** | Foreign Key | 關聯到 `cls_scdle_arnge` | 501 (哪一堂課被改了) |
    | **`cls_scdle_arnge_log_t_col`** | VarChar | 變動的欄位名稱 | `instructor_id`, `st_time`, `status` |
    | **`cls_scdle_arnge_log_old_value`** | NVarChar | 變更前的值 | admin_01 (舊老師) |
    | **`cls_scdle_arnge_log_new_value`** | NVarChar | 變更後的值 | admin_05 (新老師) |
    | **`operator_id`** | VarChar | 操作者 ID | admin_01 |
    | **`cls_scdle_arnge_log_dt`** | DateTime | 紀錄產生的時間 | 2026-03-13 14:00:00 |
    | **`cls_scdle_arnge_log_remark`** | Text | 異動原因 (選填)
    保留 |  |
- 進出紀錄表 `device_record`
    - 三叉機刷入刷出都會產生這張表
    
    | **欄位名稱** | **資料類型** | **說明** | **範例** |
    | --- | --- | --- | --- |
    | **`device_record_sn`** | PK | 流水號 | 1 |
    | **`usr_id`** | FK | 誰刷的 | U0001 |
    | **`direction`** | Int / Enum | 進或出 | 1: 進, 2: 出 |
    | **`method`** | VarChar | 感應方式 | `QR_Code`, `RFID`, `System_Open`, `Manual_Open` |
    | **`device_id`** | VarChar | 機器編號 (新增) | Gate_01, Gate_02 |
    | **`is_success`** | Boolean | 是否成功開啟 (新增) | true (成功), false (失敗) |
    | **`device_record_dt`** | DateTime | 感應時間 | 2026-03-13 19:10:00 |
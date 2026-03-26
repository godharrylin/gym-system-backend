# 註冊會員頁面 API 規格

## Description

<aside>

點擊 “+Add Member” button 後會載入的頁面。
下面是會用到的API

</aside>

- API 1: 取得票券方案列表 **`[GET] /api/v1/ticket/plan`**
    - feature description
        
        前端畫面顯示當前可購買的票券方案。
        
    - trigger timing
        
        載入註冊會員畫面自動帶入。
        
    - Process
        - 撈取 [**`ticket_plan_kind`**](https://www.notion.so/Database-New-2c20ef2839da80b6a84df886c237e15b?pvs=21) 資料表
        - 過濾資料: `ticket_plan_kind_is_active` 取得結果為`true` 的資料。
    - Response
        - 回傳票券名稱、價格、預設天數、預設堂數(如果999改為”不限次數”文字)
- API 2: 驗證會員重複註冊 **`[POST] /api/v1/users/check-phone-duplicate`**
    - feature description
        
        驗證註冊輸入的電話或號碼是否重複
        
    - trigger timing
        
        後台人員輸入框打完電話號碼，**停止輸入300ms後才會發送一次request(尚未實作)**
        
    - Process
        - 撈取 `users` 資料表
        - 比對 request 的電話號碼資料是已存在表內
    - Response
        - 回傳 true(重複電話號碼) , false(可以註冊的電話號碼)
- API 3: 會員註冊與購買票券 **`[POST] /api/v1/users/register`**
    - feature description
        
        支援單人/多批 會員註冊。若夾帶票券資訊，則同步建立票券訂單並發放票券。
        
    - trigger timing
        
        按下”註冊”按鈕
        
    - Request
    前端不要傳遞 UI 狀態如 `id`, `isDuplicate`，也不要傳遞完整的 `Product` 物件，後端只需要 ID。更不要傳實際金額，後端會自己重算。
        
        ```jsx
        {
          // 1. 會員清單 (必填，至少需有一人)
          "members": [
            {
              "name": "王小明",
              "phone": "0912345678"
            }
          ],
          
          // 2. 購票資訊 (選填，若為 null 代表「純註冊不買票」)
          "ticketPurchase": {
            "ticketPlanKindId": "T_002",      // 對應資料庫的方案 ID
            "activationDate": "2026-03-17",   // 生效日 (YYYY-MM-DD)
            "paymentStatus": "PAID"           // 列舉: "PAID" 或 "UNPAID"
          }
        }
        ```
        
    - Process
        1. 基本驗證
            - `members` 不可為null
            - 檢查 `name`, `phone` 輸入格式
        2. 唯一性防護(Race Condition防護)
            - 重新檢查一次`phone` 是否唯一
        3. 情境分流 
            - 純註冊(`ticketPurchase`為 `null`)
                
                直接進入 4. 存檔，跳過訂單票券計算。
                
            - 註冊+購買票券(`ticketPurchase`有值)
                - 透過 `ticketPlanKindId` 去資料庫撈取真實單價與預設天數/堂數。
                - **核心算錢：** 根據人數 (`members.length`) 計算總價。若人數 ≥ 2，套用 Family Plan 5% 折扣。
        4. 資料庫交易 (Database Transaction - 寫入行為)
            - **`users` 表：** 遍歷 `members` 陣列，寫入多筆學生資料。
            - **`sdt_profile` 表：** 建立每位學生的快取空殼。
            - **若為情境 B (有購票)，同 Transaction 繼續寫入：**
                - **`orders` 表：** 建立 1 筆總訂單 (實收總額為後端計算結果)。
                - **`order_items` 表：** 依人數建立多筆明細。
                - **`sdt_ticket_pass` 表：** 依人數建立多筆票券。生效日帶入 `activationDate`，到期日由後端依票券預設天數推算。
                - 更新 `sdt_profile` 的最新票券狀態 (`Active` 或 `UnActive`)。
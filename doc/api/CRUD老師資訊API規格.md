# CRUD老師資訊API規格

## Description

<aside>

關於指導老師的CRUD

</aside>

- API 1: 取得老師資訊列表 **`[GET]: /api/v1/instructors`**
    - feature description
        
        顯示老師列表。
        
    - trigger timing
        
        載入系統設定頁面時會顯示。
        
    - response
        
        回傳老師的教師編號、名字、電話、任職狀態
        
- API 2: 建立老師資訊 **`[POST]: /api/v1/instructors`**
    - Send Format
        
        ```jsx
        // 當user表已經存在手機號碼，user_role表沒有老師資料。打這個API只會新增user_role
        // 不會改動已經存在的user表名字
        {
          "name": "老師小愛"
          ,"phone": "0900000000"
        
        }
        ```
        
    - feature
        
        新增一筆老師資料
        
    - trigger timing
        
        按下"Add Instructor"頁面的 “Save”按鈕
        
    - request
        
        老師姓名、電話、任職狀態
        
    - response
        
        建立成功或失敗
        
- API 3: 修改老師資訊 **`[POST]: /api/v1/instructors/{id}`**
    - Send Format
        
        ```jsx
        // 可以只送其中一種屬令
        {
          "name": "老師小愛"
          ,"phone": "0922222221"
          ,"isEmployed" : true
        }
        ```
        
    - feature description
        
        修改老師名字和電話、任職狀態。
        
    - trigger timing
        
        在修改頁面按下"Save" 按鈕
        
    - response
        
        修改成功或失敗
        
- API4: 取得特定老師的課表 **`[GET]: /api/v1/instructors/{id}/schedules`**
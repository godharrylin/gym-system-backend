# CRUD 課程資訊

- API 1: 取得課程基本資訊列表 **`[GET]: /api/v1/classes`**
    - feature description
        
        取得課程基本資訊
        
    - trigger timing
        
        按下 `Manage Classes` button，會先載入課程基本資訊
        
    - request
    `includeInactive` 不給的話，就會抓全部
    - response
        
        ```json
        {
            "classInfoList": [
                {
                    "id": "1",
                    "name": "",
                    "instructor": "",
                    "color": "",
                    "duration": "0",
                    "isFree": false,
                    "isActive": true
                },
                {
                    "id": "2",
                    "name": "",
                    "instructor": "",
                    "color": "",
                    "duration": "0",
                    "isFree": false,
                    "isActive": true
                },
                {
                    "id": "3",
                    "name": "",
                    "instructor": "",
                    "color": "",
                    "duration": "0",
                    "isFree": true,
                    "isActive": true
                },
                {
                    "id": "4",
                    "name": "",
                    "instructor": "",
                    "color": "",
                    "duration": "0",
                    "isFree": false,
                    "isActive": true
                },
                {
                    "id": "5",
                    "name": "",
                    "instructor": "",
                    "color": "",
                    "duration": "0",
                    "isFree": false,
                    "isActive": false
                }
            ]
        }
        ```
        
- API2: 建立課程資訊 **`[POST]: /api/v1/classes`**
    - 
- API3: 更新課程資訊 **`[POST]: /api/v1/classes/{id}`**
    - feature description
        
        更新某一堂課程資訊
        
    - trigger timing
        
        按下 `Save Changes` 後會送出更改資訊
        
    - request
        
        ```json
        // 必須參數
        {
            "id": "CLS000004",
            "instructorId": "U0000000003",
            "duration": 70
        }
        /*----------------------------------*/
        {
            "id": "CLS000004",
            "name": "核心皮拉提斯",
            "instructorId": "U0000000003",
            "duration": 70,
            "color": "#581845",
            "isfree": false
        }
        ```
        
    - response
        
        ```json
        {
        	bool
        }
        ```
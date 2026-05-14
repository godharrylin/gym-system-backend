# API 命名規範（Query / UseCase）

## 目標
- 同一層職責使用同一命名語意，避免 `Service/Serivce`、`Instructor/Instrucotr` 這種 typo 造成維護成本。
- 新功能（例如 `Class`）可以直接複用相同結構。

## Namespace 與資料夾
- UseCase 一律使用複數功能名：`<Feature>sUseCase`
- 範例：
  - `gym_system.Application.InstructorsUseCase`
  - `gym_system.Application.TicketPlansUseCase`
  - `gym_system.Application.ClassesUseCase`

## Query 相關命名
- 介面：`I<Feature>CatalogQueryService`
- Handler：`Get<Feature>ListHandler` 或 `GetActive<Feature>Handler`
- Read Model：`<Feature>Result`
- Dapper 實作：`Dapper<Feature>QueryService` 或 `Dapper<Feature>CatalogQueryService`

## Controller 命名
- Controller 類別使用複數資源名：`<Features>Controller`
- 欄位/參數使用 `queryService`，避免 `querySerivce` typo

## DTO 命名
- 回傳根物件：`Get<Feature>Response`
- 單筆項目：`<Feature>Dto`

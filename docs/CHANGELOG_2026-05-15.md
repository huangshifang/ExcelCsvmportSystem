# 缺陷修复日志 — 2026-05-15

## 1. CSV HasHeaderRow=false 解析错误（ImportService.cs）

**问题**：当 `HasHeaderRow=false` 时，`ReadCsvPreview` 和 `ReadCsvData` 始终将第一行作为表头处理，导致无表头的 CSV 文件列名丢失或数据行被当作表头跳过。

**修复**：
- `ReadCsvPreview`：`HasHeaderRow=false` 时生成 `Column1`..`ColumnN` 列名，并将第一条记录作为数据读取
- `ReadCsvData`：同上逻辑，确保实际导入时第一条数据不被遗漏

**影响文件**：
| 文件 | 方法 | 变更 |
|------|------|------|
| `Infrastructure/Services/ImportService.cs` | `ReadCsvPreview()` | 增加 HasHeaderRow 条件分支 |
| `Infrastructure/Services/ImportService.cs` | `ReadCsvData()` | 增加 HasHeaderRow 条件分支 |

---

## 2. 多工作表 Excel 支持（ImportService.cs）

**问题**：代码始终读取 `Worksheets[0]`（第一个工作表）。某些 Excel 文件（如益海嘉里报表）sheet1 为空占位，实际数据在 sheet2 或更后面的工作表。导致预览返回空列，前端显示误导性「所有列已映射」提示。

**修复**：新增 `GetFirstDataWorksheet()` 辅助方法，遍历工作表找到第一个包含实际数据的工作表：
- 跳过 `Dimension == null` 的工作表
- 跳过仅有空 A1 单元格的工作表（`Dimension.Rows == 1 && Dimension.Columns == 1` 且 A1 为空）
- 回退到第一个工作表（兜底）

**影响文件**：
| 文件 | 方法 | 变更 |
|------|------|------|
| `Infrastructure/Services/ImportService.cs` | `GetFirstDataWorksheet()` | 新增辅助方法 |
| `Infrastructure/Services/ImportService.cs` | `ReadExcelPreview()` | 使用 `GetFirstDataWorksheet()` 替代 `Worksheets[0]` |
| `Infrastructure/Services/ImportService.cs` | `ReadExcelData()` | 使用 `GetFirstDataWorksheet()` 替代 `Worksheets[0]` |

---

## 3. 前端空列防御性检查（ImportPage.tsx）

**问题**：
- `preview?.excelColumns.filter(...)` 在 `preview` 为 null 时调用 `.filter()` 会抛出 TypeError，可能导致组件崩溃
- 后端返回空列时，`unmappedExcelCols` 为空数组，前端显示绿色「所有列已映射」成功提示，但映射表格为空（无内容），用户体验矛盾

**修复**：
- 引入安全变量 `const excelColumns = preview?.excelColumns ?? []`，防止 null 引用崩溃
- `excelColumns.length === 0` 时显示红色错误提示「无法读取到文件列，请确认文件已选择并重新点击下一步」，替代误导性的绿色成功提示
- 映射状态 Alert 移至表格下方，用户先看映射内容再看状态

**影响文件**：
| 文件 | 变更 |
|------|------|
| `frontend/src/pages/Import/ImportPage.tsx` | 安全变量 + 空列分支 + Alert 位置调整 |
| `frontend/src/i18n/locales/zh.json` | 新增 `import.noColumnsFound` |
| `frontend/src/i18n/locales/en.json` | 新增 `import.noColumnsFound` |

---

## 4. i18n 新增键

| 键 | 中文 | 英文 |
|----|------|------|
| `import.noColumnsFound` | 无法读取到文件列，请确认文件已选择并重新点击下一步。 | No readable columns found. Please confirm the file is selected and retry. |

---

## 本次修复文件汇总

| 文件 | 变更类型 |
|------|----------|
| `src/ExcelImportSystem.Infrastructure/Services/ImportService.cs` | CSV 修复 + 多工作表支持 |
| `frontend/src/pages/Import/ImportPage.tsx` | 空值防御 + 空列处理 + UX 优化 |
| `frontend/src/i18n/locales/zh.json` | 新增翻译键 |
| `frontend/src/i18n/locales/en.json` | 新增翻译键 |

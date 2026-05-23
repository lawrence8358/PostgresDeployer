namespace PostgresDeployer.Core.Models;

/// <summary>
/// Schema 差異變更類型。所有類型皆會自動執行。
/// </summary>
public enum ChangeType
{
    /// <summary>建立新資料表（整個 CREATE TABLE）</summary>
    CreateTable,

    /// <summary>新增欄位（ALTER TABLE ADD COLUMN）</summary>
    AddColumn,

    /// <summary>變更欄位型別（ALTER TABLE ALTER COLUMN TYPE）</summary>
    AlterColumnType,

    /// <summary>變更欄位 Nullable 屬性（SET/DROP NOT NULL）</summary>
    AlterColumnNullable,

    /// <summary>變更欄位預設值（SET/DROP DEFAULT）</summary>
    AlterColumnDefault,

    /// <summary>建立新索引（CREATE INDEX）</summary>
    CreateIndex,

    /// <summary>重建索引（DROP + CREATE，因索引定義已變更）</summary>
    RecreateIndex,

    /// <summary>刪除索引（DB 有但 SQL 檔沒有的索引）</summary>
    DropIndex,

    /// <summary>刪除欄位（DB 有但 SQL 定義檔沒有，ALTER TABLE DROP COLUMN）</summary>
    DropColumn,

    /// <summary>重建主鍵（DROP CONSTRAINT + ADD CONSTRAINT PRIMARY KEY）</summary>
    RecreatePrimaryKey,

    /// <summary>建立新視圖（View 不存在於資料庫）</summary>
    CreateView,

    /// <summary>取代視圖（先 DROP IF EXISTS CASCADE，再重建，因 View 定義已變更）</summary>
    ReplaceView,
}

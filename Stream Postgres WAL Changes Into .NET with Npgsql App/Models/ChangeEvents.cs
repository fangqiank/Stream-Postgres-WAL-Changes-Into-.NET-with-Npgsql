using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models
{
    /// <summary>
    /// 数据库变更事件
    /// </summary>
    public class ChangeEvent
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public ChangeEventType EventType { get; set; }

        /// <summary>
        /// 模式名称
        /// </summary>
        public string SchemaName { get; set; } = "public";

        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 变更前数据（JSON格式）
        /// </summary>
        public string? BeforeData { get; set; }

        /// <summary>
        /// 变更后数据（JSON格式）
        /// </summary>
        public string? AfterData { get; set; }

        /// <summary>
        /// 变更的列
        /// </summary>
        public List<string> ChangedColumns { get; set; } = new();

        /// <summary>
        /// 事务ID
        /// </summary>
        public string? TransactionId { get; set; }

        /// <summary>
        /// LSN（日志序列号）
        /// </summary>
        public string? Lsn { get; set; }

        /// <summary>
        /// 事件时间戳
        /// </summary>
        public DateTime EventTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 提交时间戳
        /// </summary>
        public DateTime CommitTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 操作用户
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// 元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 获取解析后的Before数据
        /// </summary>
        public T? GetBeforeData<T>() where T : class
        {
            return string.IsNullOrEmpty(BeforeData) ? null : JsonSerializer.Deserialize<T>(BeforeData);
        }

        /// <summary>
        /// 获取解析后的After数据
        /// </summary>
        public T? GetAfterData<T>() where T : class
        {
            return string.IsNullOrEmpty(AfterData) ? null : JsonSerializer.Deserialize<T>(AfterData);
        }

        /// <summary>
        /// 是否为关键表的事件
        /// </summary>
        public bool IsCriticalTable()
        {
            var criticalTables = new[] { "orders", "users", "payments", "outboxevents" };
            return criticalTables.Contains(TableName.ToLowerInvariant());
        }
    }

    /// <summary>
    /// 变更事件类型
    /// </summary>
    public enum ChangeEventType
    {
        Insert,
        Update,
        Delete,
        Truncate
    }

    /// <summary>
    /// 表结构信息
    /// </summary>
    public class TableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = "public";
        public List<ColumnInfo> Columns { get; set; } = new();
        public List<string> PrimaryKeys { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 列信息
    /// </summary>
    public class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string? DefaultValue { get; set; }
        public int MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
    }
}
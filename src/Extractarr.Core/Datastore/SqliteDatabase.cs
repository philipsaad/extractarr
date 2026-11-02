using System.Data;
using Microsoft.Data.Sqlite;
using Extractarr.Common.EnvironmentInfo;

namespace Extractarr.Core.Datastore;

public abstract class ModelBase
{
    public int Id { get; set; }
}

public class PagingSpec<TModel>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string SortKey { get; set; } = "Id";
    public string SortDirection { get; set; } = "descending";
    public int TotalRecords { get; set; }
    public List<TModel> Records { get; set; } = new();
}

public interface IDatabase
{
    IDbConnection OpenConnection();
    string ConnectionString { get; }
}

public class SqliteDatabase : IDatabase
{
    public string ConnectionString { get; }

    public SqliteDatabase(IAppFolderInfo appFolderInfo)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = appFolderInfo.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 30
        };
        ConnectionString = builder.ToString();
    }

    public IDbConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return connection;
    }
}


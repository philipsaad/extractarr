using System.Reflection;
using Dapper;

namespace Extractarr.Core.Datastore;

public interface IBasicRepository<TModel> where TModel : ModelBase, new()
{
    IEnumerable<TModel> All();
    int Count();
    TModel Get(int id);
    TModel? Find(int id);
    TModel Insert(TModel model);
    TModel Update(TModel model);
    void Upsert(TModel model);
    void Delete(int id);
    void DeleteMany(IEnumerable<int> ids);
    PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec, string? whereClause = null, object? parameters = null);
}

public abstract class BasicRepository<TModel> : IBasicRepository<TModel> where TModel : ModelBase, new()
{
    protected readonly IDatabase _database;
    protected readonly string _table;
    private readonly PropertyInfo[] _properties;

    protected BasicRepository(IDatabase database, string? tableName = null)
    {
        _database = database;
        _table = tableName ?? GetDefaultTableName();
        _properties = typeof(TModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(ModelBase.Id))
            .ToArray();
    }

    private static string GetDefaultTableName()
    {
        var name = typeof(TModel).Name;
        return name.EndsWith('y') ? name[..^1] + "ies" : name + "s";
    }

    public virtual IEnumerable<TModel> All()
    {
        using var conn = _database.OpenConnection();
        return conn.Query<TModel>($"SELECT * FROM \"{_table}\" ORDER BY \"Id\" ASC").ToList();
    }

    public virtual int Count()
    {
        using var conn = _database.OpenConnection();
        return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{_table}\"");
    }

    public virtual TModel Get(int id)
    {
        var item = Find(id);
        if (item == null)
        {
            throw new KeyNotFoundException($"{typeof(TModel).Name} with ID {id} was not found in {_table}.");
        }

        return item;
    }

    public virtual TModel? Find(int id)
    {
        using var conn = _database.OpenConnection();
        return conn.QuerySingleOrDefault<TModel>($"SELECT * FROM \"{_table}\" WHERE \"Id\" = @Id", new { Id = id });
    }

    public virtual TModel Insert(TModel model)
    {
        using var conn = _database.OpenConnection();
        var columns = string.Join(", ", _properties.Select(p => $"\"{p.Name}\""));
        var values = string.Join(", ", _properties.Select(p => $"@{p.Name}"));
        var sql = $"INSERT INTO \"{_table}\" ({columns}) VALUES ({values}); SELECT last_insert_rowid();";
        model.Id = conn.ExecuteScalar<int>(sql, model);
        return model;
    }

    public virtual TModel Update(TModel model)
    {
        using var conn = _database.OpenConnection();
        var setClause = string.Join(", ", _properties.Select(p => $"\"{p.Name}\" = @{p.Name}"));
        var sql = $"UPDATE \"{_table}\" SET {setClause} WHERE \"Id\" = @Id";
        conn.Execute(sql, model);
        return model;
    }

    public virtual void Upsert(TModel model)
    {
        if (model.Id == 0)
        {
            Insert(model);
        }
        else
        {
            Update(model);
        }
    }

    public virtual void Delete(int id)
    {
        using var conn = _database.OpenConnection();
        conn.Execute($"DELETE FROM \"{_table}\" WHERE \"Id\" = @Id", new { Id = id });
    }

    public virtual void DeleteMany(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0)
        {
            return;
        }

        using var conn = _database.OpenConnection();
        conn.Execute($"DELETE FROM \"{_table}\" WHERE \"Id\" IN @Ids", new { Ids = list });
    }

    public virtual PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec, string? whereClause = null, object? parameters = null)
    {
        using var conn = _database.OpenConnection();
        var validSortProps = _properties.Select(p => p.Name).Append("Id").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortCol = validSortProps.FirstOrDefault(p => p.Equals(pagingSpec.SortKey, StringComparison.OrdinalIgnoreCase)) ?? "Id";
        var sortDir = pagingSpec.SortDirection.Equals("ascending", StringComparison.OrdinalIgnoreCase) ||
                      pagingSpec.SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? "ASC"
            : "DESC";

        var whereSql = string.IsNullOrWhiteSpace(whereClause) ? string.Empty : $"WHERE {whereClause}";
        var page = Math.Max(1, pagingSpec.Page);
        var pageSize = Math.Clamp(pagingSpec.PageSize, 1, 500);
        var offset = (page - 1) * pageSize;

        pagingSpec.TotalRecords = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{_table}\" {whereSql}", parameters);

        var dynParams = new DynamicParameters(parameters);
        dynParams.Add("__Limit", pageSize);
        dynParams.Add("__Offset", offset);

        pagingSpec.Records = conn.Query<TModel>(
            $"SELECT * FROM \"{_table}\" {whereSql} ORDER BY \"{sortCol}\" {sortDir} LIMIT @__Limit OFFSET @__Offset",
            dynParams).ToList();

        return pagingSpec;
    }
}


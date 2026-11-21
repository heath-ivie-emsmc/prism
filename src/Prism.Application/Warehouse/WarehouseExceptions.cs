namespace Prism.Application.Warehouse;

/// <summary>The warehouse is not configured or did not answer. The message never carries the connection string or the SQL.</summary>
public sealed class WarehouseUnavailableException : Exception
{
    public WarehouseUnavailableException()
        : base("The data warehouse is not available.")
    {
    }

    public WarehouseUnavailableException(Exception inner)
        : base("The data warehouse is not available.", inner)
    {
    }
}

/// <summary>A query broke a rule: no Provider filter, a missing parameter, or text that is not a single read. This is a bug in the caller.</summary>
public sealed class WarehouseQueryRejectedException(string reason) : Exception(reason);

/// <summary>A query returned more rows than the cap. Aggregate in SQL instead of returning detail.</summary>
public sealed class WarehouseResultTooLargeException(int maxRows) : Exception($"The query returned more than {maxRows} rows.");

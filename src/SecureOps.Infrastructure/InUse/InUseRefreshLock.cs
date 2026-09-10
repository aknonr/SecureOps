using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SecureOps.Infrastructure.InUse;

// A transaction-owned application lock survives multiple API instances but holds no inventory row locks.
internal sealed class InUseRefreshLock(SqlConnection connection, SqlTransaction transaction) : IAsyncDisposable
{
    internal static async Task<IAsyncDisposable?> TryAcquireAsync(string connectionString, CancellationToken token)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(token);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var lease = new InUseRefreshLock(connection, transaction);
            try
            {
                int result = await connection.QuerySingleAsync<int>(new CommandDefinition("""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource = N'SecureOps:InUseRefresh:4241:68',
                        @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
                    SELECT @result;
                    """, transaction: transaction, commandTimeout: 5, cancellationToken: token));
                if (result >= 0)
                { return lease; }
                if (result != -1)
                { throw new InvalidOperationException("In Use scope lock unavailable."); }
            }
            catch
            {
                await lease.DisposeAsync();
                throw;
            }
            await lease.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        { await transaction.DisposeAsync(); }
        finally { await connection.DisposeAsync(); }
    }
}

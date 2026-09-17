namespace SecureOps.Tests.Integration.Sql;

public sealed class Rc621DefectSqlFactAttribute : FactAttribute
{
    public Rc621DefectSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREOPS_RC621_DEFECT_CONNECTION")))
        { Skip = "Requires isolated Rc621Defects LocalDB schema 001-021; never a corporate connection."; }
    }
}

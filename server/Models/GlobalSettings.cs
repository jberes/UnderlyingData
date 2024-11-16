namespace RevealSdk.SqlServer.Models
{
    public static class GlobalSettings
    {
        public static string? RevealLicenseKey { get; set; }
        public static string? OnPremSqlServer { get; set;}
        public static string? OnPremSqlServerUid { get; set;}
        public static string? OnPremSqlServerPassword { get;set;}
        public static string? AzureSqlServer { get; set; }
        public static string? AzureSqlServerUid { get; set; }
        public static string? AzureSqlServerPassword { get; set; }
        public static string? OnPremPostgresServer { get; set; }
        public static string? OnPremPostgresServerUid { get; set; }
        public static string? OnPremPostgresServerPassword { get; set; }
    }
}
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;


namespace NovaWallet.Api.HealthChecks
{
    public class DbHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        public DbHealthCheck(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // check SQL health
            try
            {
                using (var connection = new SqlConnection(_configuration.GetConnectionString("NovaWalletDb")))
                {
                    await connection.OpenAsync(cancellationToken);
                }
                return HealthCheckResult.Healthy("SQL is reachable");
            }
            catch (Exception ex)
            {
                Activity.Current?.AddTag("DbHealthCheck.CheckHealthAsync.Exception :::: ", ex.Message);

                return new HealthCheckResult(
                    context.Registration.FailureStatus, $"SQL error, {ex.Message}");
            }
        }
    }
}

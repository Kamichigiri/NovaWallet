using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace NovaWallet.Api.ServiceExtentions
{
    public static class RateLimiterConfig
    {
        public const string PolicyName = "IpSlidingWindow";
        public static int PermitLimit = 100;
        public static int WindowSeconds = 60;
        public static int SegmentsPerWindow = 4;
        public static int QueueLimit = 0;


        public static IServiceCollection AddIpRateLimiter(
        this IServiceCollection services,
        IConfiguration configuration)
        {
            var tokenLimit = configuration.GetValue<int>(
            "RateLimiting:TokenLimit", 100);

            var tokensPerPeriod = configuration.GetValue<int>(
                "RateLimiting:TokensPerPeriod", 10);

            var replenishmentPeriodSeconds = configuration.GetValue<int>(
                "RateLimiting:ReplenishmentPeriodSeconds", 1);

            var queueLimit = configuration.GetValue<int>(
                "RateLimiting:QueueLimit", 0);

            services.AddRateLimiter(options =>
            {
                options.AddPolicy(PolicyName, httpContext =>
                {
                    var ip = GetClientIp(httpContext);

                    Console.WriteLine(
                        $"Validating rate limiter for IP: {ip}");

                    return RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: ip,
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = tokenLimit,

                            TokensPerPeriod = tokensPerPeriod,

                            ReplenishmentPeriod =
                                TimeSpan.FromSeconds(
                                    replenishmentPeriodSeconds),

                            AutoReplenishment = true,

                            QueueProcessingOrder =
                                QueueProcessingOrder.OldestFirst,

                            QueueLimit = queueLimit
                        });
                });

                options.RejectionStatusCode =
                    StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var httpContext = context.HttpContext;
                    var response = httpContext.Response;

                    response.StatusCode =
                        StatusCodes.Status429TooManyRequests;

                    response.ContentType = "application/json";

                    if (context.Lease.TryGetMetadata(
                            MetadataName.RetryAfter,
                            out var retryAfter))
                    {
                        response.Headers.RetryAfter =
                            Math.Ceiling(
                                retryAfter.TotalSeconds)
                            .ToString();
                    }

                    response.Headers.TryAdd( "X-XSS-Protection", "1; mode=block");

                    response.Headers.TryAdd( "X-Frame-Options", "SAMEORIGIN");

                    response.Headers.TryAdd( "X-Content-Type-Options", "nosniff");

                    response.Headers.TryAdd( "Content-Security-Policy", "frame-ancestors 'self'");

                    var result = new
                    {
                        status = "failed",
                        error = "Rate limit exceeded.",
                        message = "You have exceeded the allowed number of requests. Please wait and try again.",
                        statusCode = StatusCodes.Status429TooManyRequests,
                        traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier
                    };

                    await response.WriteAsync(
                        JsonSerializer.Serialize(result),
                        cancellationToken);
                };
            });

            return services;


        }
        private static string GetClientIp(HttpContext context)
        {
            // Respect forwarded header from reverse proxies
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
            {
                // Take the first (originating) IP in the chain
                var firstIp = forwarded.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(firstIp))
                    return firstIp;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}

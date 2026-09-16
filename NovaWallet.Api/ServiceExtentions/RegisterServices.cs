using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NovaWallet.Api.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NovaWallet.Api.ServiceExtentions
{
    public static class RegisterServices
    {
        public static void ConfigureTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOpenTelemetry()
                .WithTracing(tracerProviderBuilder =>
                    tracerProviderBuilder
                        .SetResourceBuilder(
                            ResourceBuilder.CreateDefault().AddService("NovaWallet.Api"))
                        .ConfigureResource(resource => resource
                        .AddService("NovaWallet.Api"))
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.Filter = (httpContext) =>
                            {
                                var path = httpContext.Request.Path;

                                // Exclude specific endpoints
                                if (path.StartsWithSegments("/health"))
                                {
                                    return false;
                                }

                                return true;
                            };
                        })
                        .AddHttpClientInstrumentation()
                        //.AddOtlpExporter(opts => { opts.Endpoint = new Uri(configuration["jaeger"]); })
                        .AddConsoleExporter()
                        );

            services.AddHealthChecks()
                .AddCheck<DbHealthCheck>("SQL Database");
            //    .AddCheck<PostingHealthCheck>("Posting API");
        }

        public static void ConfigureSwagger(this IServiceCollection services)
        {
            #region Swagger
            services.AddSwaggerGen(c =>
            {

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Authorization header using the Bearer scheme."
                }
                 );

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                                {
                                    new OpenApiSecurityScheme
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Type = ReferenceType.SecurityScheme,
                                            Id = "Bearer"
                                        },
                                        Scheme = "Bearer",
                                        Name = "Authorization",
                                        In = ParameterLocation.Header
                                    },
                                    Array.Empty<string>() // Use an empty array to indicate no specific scopes are required
                                }
                });

            });

            #endregion

            #region Json Options
            //services.AddControllers()
            //    .AddJsonOptions(options =>
            //    {
            //        options.JsonSerializerOptions.PropertyNamingPolicy = null;
            //        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
            //    });
            #endregion

        }

    }
}

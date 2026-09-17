using System.Diagnostics;
using System.Text.Json;
using NovaWallet.Application.Exceptions;

namespace NovaWallet.Api;

public sealed class ProblemDetailsMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (AppException ex) { await Write(context, ex.StatusCode, ex.Code, ex.Message); }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILogger<ProblemDetailsMiddleware>>().LogError(ex, "Unhandled request error");
            await Write(context, 500, "internal_error", "An unexpected error occurred.");
        }
    }

    private static async Task Write(HttpContext ctx, int status, string code, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = $"https://httpstatuses.com/{status}", title = code, status, detail, traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier
        }));
    }
}

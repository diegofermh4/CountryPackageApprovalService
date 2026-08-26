using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Country_PackageAPI.Swagger;

/// <summary>Adds the optional <c>Idempotency-Key</c> header to every action decorated with
/// <see cref="IdempotentActionAttribute"/> (the two step-transition endpoints), so testers see it documented
/// in Swagger UI instead of finding it only in README.md - see docs/ARCHITECTURE.md §3.3.</summary>
public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isIdempotent = context.MethodInfo
            .GetCustomAttributes(typeof(IdempotentActionAttribute), inherit: true)
            .Any();
        if (!isIdempotent) return;

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "Optional client-generated key. Retrying the same operation on the same " +
                          "package/step with the same key returns the original result instead of re-executing it."
        });
    }
}

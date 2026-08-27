namespace Country_PackageAPI.Swagger;

/// <summary>Marks a controller action that accepts an optional <c>Idempotency-Key</c> header, so
/// <see cref="IdempotencyKeyOperationFilter"/> can document that header in Swagger without hardcoding route
/// names or guessing from HTTP verbs.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentActionAttribute : Attribute
{
}

using CountryPackageApprovalService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Country_PackageAPI.Controllers;

/// <summary>
/// Testing convenience only: lists every seeded fictional user and their country/role grants, so whoever is
/// exercising this API through Swagger can pick an <c>X-User-Id</c> value without digging GUIDs out of
/// README.md by hand. There is no equivalent "list every user" operation in the Azure target production API
/// (docs/ARCHITECTURE.md §6.3) - this endpoint exists only because the take-home substitutes a request header
/// for real Entra ID authentication, which otherwise gives every caller their own identity for free.
/// </summary>
[ApiController]
[Route("api/v1/test-users")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class TestUsersController : ControllerBase
{
    private readonly IUserRepository _users;

    public TestUsersController(IUserRepository users) => _users = users;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TestUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TestUserDto>>> GetAll(CancellationToken ct)
    {
        var users = await _users.GetAllAsync(ct);

        var dtos = users
            .Select(u => new TestUserDto(
                u.Id,
                u.DisplayName,
                u.Email,
                u.Roles
                    .Select(r => new TestUserRoleDto(r.CountryCode, r.Role.ToString(), r.OrgLevel.ToString()))
                    .ToList()))
            .ToList();

        return Ok(dtos);
    }
}

/// <summary>Testing-only projection of a seeded user - see <see cref="TestUsersController"/>.</summary>
public sealed record TestUserDto(Guid Id, string DisplayName, string Email, IReadOnlyList<TestUserRoleDto> Roles);

/// <summary>One country/role/org-level grant held by a seeded test user.</summary>
public sealed record TestUserRoleDto(string CountryCode, string Role, string OrgLevel);

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Dto.ApplicationErrors;
using Proxytrace.Domain.ApplicationError;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// Read-only access to captured application errors for the Error Log UI. Admin-only: stacktraces
/// can contain sensitive runtime detail.
/// </summary>
[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/error-log")]
public class ApplicationErrorsController : ControllerBase
{
    private readonly IApplicationErrorRepository repository;

    public ApplicationErrorsController(IApplicationErrorRepository repository)
    {
        this.repository = repository;
    }

    [HttpGet]
    public async Task<PagedResult<ApplicationErrorDto>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] ApplicationErrorLevel? level = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var paged = await repository.GetPagedNewestFirstAsync(page, pageSize, level, search, from, to, cancellationToken);
        return paged.Map(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationErrorDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var error = await repository.FindAsync(id, cancellationToken);
        if (error is null)
        {
            return NotFound();
        }

        return ToDto(error);
    }

    private static ApplicationErrorDto ToDto(IApplicationError e) =>
        new(e.Id, e.Message, e.Level, e.Category, e.ExceptionType, e.StackTrace, e.CreatedAt);
}

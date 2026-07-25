using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Shared ErrorOr→HTTP mapping and inline FluentValidation (SP-3). Generic error shapes keep
/// responses non-enumerating (BR-AUTH-3).</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected async Task<IActionResult?> ValidateAsync<T>(T model, CancellationToken ct)
    {
        var validator = HttpContext.RequestServices.GetService(typeof(IValidator<T>)) as IValidator<T>;
        if (validator is null) return null;
        var result = await validator.ValidateAsync(model, ct);
        if (result.IsValid) return null;
        return ValidationProblem(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    protected IActionResult Respond<T>(ErrorOr<T> result, Func<T, IActionResult> onSuccess) =>
        result.IsError ? Problem(result.Errors) : onSuccess(result.Value);

    protected IActionResult Respond(ErrorOr<Success> result) =>
        result.IsError ? Problem(result.Errors) : Ok();

    protected IActionResult Problem(List<Error> errors)
    {
        var first = errors[0];
        var status = first.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return Problem(detail: first.Description, statusCode: status, title: first.Code);
    }
}

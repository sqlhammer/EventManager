using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Hub.Controllers;

[ApiController]
public abstract class HubControllerBase : ControllerBase
{
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

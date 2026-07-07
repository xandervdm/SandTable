using SandTable.Engine;

namespace SandTable.Api;

public static class ApiProblemResults
{
    public static IResult From(ApiValidationException exception)
    {
        if (exception.Errors.Count > 0)
        {
            return Results.ValidationProblem(
                exception.Errors,
                title: exception.Title,
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Problem(
            title: exception.Title,
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }

    public static IResult From(TensionChoiceValidationException exception)
    {
        return Results.Problem(
            title: "Invalid tension choice",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
}

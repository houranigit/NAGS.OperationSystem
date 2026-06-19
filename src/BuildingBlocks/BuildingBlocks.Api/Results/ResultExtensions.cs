using BuildingBlocks.Domain.Results;
using Microsoft.AspNetCore.Http;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace BuildingBlocks.Api.Results;

/// <summary>Maps <see cref="Result"/> outcomes to standard minimal-API HTTP responses.</summary>
public static class ResultExtensions
{
    public static IResult ToNoContent(this Result result) =>
        result.IsSuccess ? HttpResults.NoContent() : ApiResults.Problem(result.Error);

    public static IResult ToOk<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? HttpResults.Ok(result.Value) : ApiResults.Problem(result.Error);

    public static IResult ToOk<TValue, TOut>(this Result<TValue> result, Func<TValue, TOut> map) =>
        result.IsSuccess ? HttpResults.Ok(map(result.Value)) : ApiResults.Problem(result.Error);

    public static IResult ToCreated<TValue>(this Result<TValue> result, Func<TValue, string> location) =>
        result.IsSuccess ? HttpResults.Created(location(result.Value), result.Value) : ApiResults.Problem(result.Error);
}

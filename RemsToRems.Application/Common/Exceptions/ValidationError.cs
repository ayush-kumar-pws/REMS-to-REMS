namespace RemsToRems.Application.Common.Exceptions
{
    /// <summary>
    /// One field that failed validation, as returned to the caller: the JSON field name, a short fixed reason
    /// (never the submitted value) and the partner error code.
    /// </summary>
    public sealed record ValidationError(string Field, string Reason, int Code);
}

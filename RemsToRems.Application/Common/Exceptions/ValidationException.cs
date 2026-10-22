namespace RemsToRems.Application.Common.Exceptions
{
    /// <summary>
    /// The request failed use-case validation. The API returns 400 with <see cref="Errors"/>.
    /// </summary>
    public sealed class ValidationException : Exception
    {
        public ValidationException(IReadOnlyList<string> errors)
            : base(string.Join(" ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }
}

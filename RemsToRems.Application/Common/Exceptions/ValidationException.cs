namespace RemsToRems.Application.Common.Exceptions
{
    /// <summary>
    /// The request failed use-case validation. The API returns 400 with one entry per field in <see cref="Errors"/>.
    /// </summary>
    public sealed class ValidationException : Exception
    {
        public ValidationException(IReadOnlyList<ValidationError> errors, Guid? requestId = null)
            : base(string.Join(" ", errors.Select(error => $"{error.Field}: {error.Reason} ({error.Code}).")))
        {
            Errors = errors;
            RequestId = requestId;
        }

        public IReadOnlyList<ValidationError> Errors { get; }

        /// <summary>The request's requestId when it was valid, so the error response can echo it.</summary>
        public Guid? RequestId { get; }
    }
}

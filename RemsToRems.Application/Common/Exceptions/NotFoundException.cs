namespace RemsToRems.Application.Common.Exceptions
{
    /// <summary>
    /// Something the caller named (an environment, an organization) does not exist. The API returns 404.
    /// </summary>
    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message)
            : base(message)
        {
        }

        public NotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

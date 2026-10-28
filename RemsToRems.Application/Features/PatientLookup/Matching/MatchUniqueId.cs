using System.Security.Cryptography;

namespace RemsToRems.Application.Features.PatientLookup.Matching
{
    /// <summary>
    /// The REMS ID issued for a released match: SHA-256 of a new random GUID, first 128 bits as
    /// 32 lowercase hex characters (e.g. "9f2c1a7e5b3d48e6a1c0d7f4b2e86a35").
    /// Not derived from the patient, not sequential, not guessable.
    /// </summary>
    public static class MatchUniqueId
    {
        public static string New()
        {
            var hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }
    }
}

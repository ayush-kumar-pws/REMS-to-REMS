using RemsToRems.Application.Features.PatientLookup.Matching;

namespace RemsToRems.Application.Tests.PatientLookup
{
    /// <summary>
    /// Step 1 name cleanup for the request. Comparing and classifying (SOUNDEX, 1-edit check, tier)
    /// run in SQL, in PatientLookupRepository, which cleans stored records the same way.
    /// </summary>
    public sealed class PatientMatcherTests
    {
        // The same names are checked against the SQL normalization in PatientLookupRepository;
        // both must give these results. Keep the two lists in step.
        [Theory]
        [InlineData("Dr. Mrs. Mary-Ann Lou", "Van Der Berg Jr. III", "MARYANN", "VANDERBERG")]
        [InlineData("Dr", "Jr", "", "JR")]                           // only a title: nothing left; only a suffix: kept
        [InlineData("Mr. John", "Smith, Jr.", "JOHN", "SMITH")]
        [InlineData("Dr - Mary", "Smith - Jr", "MARY", "SMITH")]
        [InlineData("Mary-Ann", "O'Brien", "MARYANN", "OBRIEN")]
        [InlineData("D’Angelo", "O’Brien", "DANGELO", "OBRIEN")]      // curly apostrophe, same as '
        [InlineData("J.R.", "St.John", "JR", "STJOHN")]              // periods removed without a space
        [InlineData("  john  ", "  smith  ", "JOHN", "SMITH")]
        [InlineData("Prof Dr Mr Jane", "Doe Sr Jr III", "JANE", "DOE")]   // every title and suffix dropped
        public void Normalize_SameAsSql(string first, string last, string expectedFirst, string expectedLast)
        {
            Assert.Equal(expectedFirst, PatientNameNormalizer.NormalizeFirstName(PatientNameNormalizer.ToAscii(first)));
            Assert.Equal(expectedLast, PatientNameNormalizer.NormalizeLastName(PatientNameNormalizer.ToAscii(last)));
        }

        [Theory]
        [InlineData("José", "Jose")]
        [InlineData("José", "Jose")]            // decomposed accent
        [InlineData("Ærøskøbing", "AEroskobing")]
        [InlineData("O’Brien", "O'Brien")]
        [InlineData("OʼBrien", "O'Brien")]
        [InlineData("Mary—Ann", "Mary-Ann")]     // em dash
        [InlineData("Smith3", "Smith3")]              // digits are left for the validator to reject
        [InlineData("Иван", "Иван")]                  // not Latin: left as is
        public void ToAscii_FoldsLatinLettersQuotesAndDashes(string value, string expected)
        {
            Assert.Equal(expected, PatientNameNormalizer.ToAscii(value));
        }

        // The request ZIP is validated as exactly 5 digits; normalizing only trims it.
        [Theory]
        [InlineData("12345", "12345")]
        [InlineData(" 01234 ", "01234")]
        public void Normalize_Zip(string value, string expected)
        {
            Assert.Equal(expected, PatientNameNormalizer.NormalizeZip(value));
        }
    }
}

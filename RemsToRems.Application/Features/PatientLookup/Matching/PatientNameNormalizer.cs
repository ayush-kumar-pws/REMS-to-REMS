using System.Globalization;
using System.Text;

namespace RemsToRems.Application.Features.PatientLookup.Matching
{
    /// <summary>
    /// Step 1 of the REMS-to-REMS patient match: turn request names into comparable values.
    /// PatientLookupValidator calls ToAscii, checks the allowed characters, then NormalizeFirstName /
    /// NormalizeLastName. Stored records are cleaned the same way in SQL (PatientLookupRepository),
    /// except that SQL does not fold accents yet.
    /// </summary>
    public static class PatientNameNormalizer
    {
        private static readonly HashSet<string> Titles = new(StringComparer.Ordinal)
        {
            "MR", "MRS", "MS", "MISS", "MX", "DR", "PROF"
        };

        private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
        {
            "JR", "SR", "II", "III", "IV", "V"
        };

        // Curly quotes and the modifier-letter apostrophe that phones and word processors insert.
        private static readonly HashSet<char> Apostrophes = new() { '‘', '’', 'ʼ' };

        // Hyphen, non-breaking hyphen, figure dash, en dash, em dash, horizontal bar, minus sign.
        private static readonly HashSet<char> Dashes = new() { '‐', '‑', '‒', '–', '—', '―', '−' };

        // Latin letters with no Unicode decomposition, folded as CLDR Latin-ASCII does.
        private static readonly Dictionary<char, string> LatinLetters = new()
        {
            ['Æ'] = "AE", ['æ'] = "ae", ['Œ'] = "OE", ['œ'] = "oe", ['Ø'] = "O", ['ø'] = "o",
            ['Đ'] = "D", ['đ'] = "d", ['Ð'] = "D", ['ð'] = "d", ['Ł'] = "L", ['ł'] = "l",
            ['Ŀ'] = "L", ['ŀ'] = "l", ['Ħ'] = "H", ['ħ'] = "h", ['Ŧ'] = "T", ['ŧ'] = "t",
            ['Þ'] = "TH", ['þ'] = "th", ['Ŋ'] = "N", ['ŋ'] = "n", ['ß'] = "ss", ['ı'] = "i", ['ſ'] = "s"
        };

        /// <summary>
        /// Before the character check: accents removed ("José" and "Jose" + combining accent both give "Jose"),
        /// curly apostrophes become ', long dashes become -, and letters like Ø and ß are spelled in ASCII.
        /// Anything else (digits, symbols, non-Latin letters) is left as it is, so the validator rejects it.
        /// </summary>
        public static string ToAscii(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (Apostrophes.Contains(c))
                {
                    builder.Append('\'');
                }
                else if (Dashes.Contains(c))
                {
                    builder.Append('-');
                }
                else if (LatinLetters.TryGetValue(c, out var ascii))
                {
                    builder.Append(ascii);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Uppercase, drop punctuation and all leading titles, keep the first word, join hyphens.
        /// "Dr. Mrs. Mary-Ann Lou" -> "MARYANN". A name of titles only ("Dr.") gives "", which the validator
        /// rejects. Same rule as SQL in PatientLookupRepository.
        /// </summary>
        public static string NormalizeFirstName(string value)
        {
            return Words(value).SkipWhile(Titles.Contains).FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// Uppercase, drop punctuation and all trailing suffixes, join hyphens and spaces.
        /// "Van Der Berg Jr. III" -> "VANDERBERG". A suffix is dropped only while another word remains
        /// ("Jr" -> "JR"). Same rule as SQL in PatientLookupRepository.
        /// </summary>
        public static string NormalizeLastName(string value)
        {
            var words = Words(value);
            while (words.Count > 1 && Suffixes.Contains(words[^1]))
            {
                words.RemoveAt(words.Count - 1);
            }

            return string.Concat(words);
        }

        /// <summary>The request ZIP is exactly 5 digits (validated); only surrounding spaces are removed.</summary>
        public static string NormalizeZip(string value) => value.Trim();

        // Apostrophes and periods are removed without a space (O'Brien -> OBRIEN, J.R. -> JR);
        // commas separate words. Input is ToAscii output, so the only apostrophe left is '.
        private static List<string> Words(string value) => value
            .Trim()
            .ToUpperInvariant()
            .Replace("'", string.Empty)
            .Replace(".", string.Empty)
            .Replace(",", " ")
            .Replace("-", string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RemsToRems.Application.Abstractions.Database;
using RemsToRems.Application.Abstractions.Repositories;
using RemsToRems.Application.Features.PatientLookup;
using RemsToRems.Application.Features.PatientLookup.Matching;
using RemsToRems.Infrastructure.AzureBlob;
using RemsToRems.Infrastructure.Database.Abstractions;
using RemsToRems.Infrastructure.Database.Models;
using System.Data.Common;

namespace RemsToRems.Infrastructure.Repositories
{
    internal sealed class PatientLookupRepository : IPatientLookupRepository
    {
        private const string UserRoleName = "REMS Patient";

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDbCommandExecutor _commandExecutor;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration;
        private readonly PatientMatchOptions _matchOptions;
        private readonly DatabaseConnectionOptions _connections;

        public PatientLookupRepository(
            IDbConnectionFactory connectionFactory,
            IDbCommandExecutor commandExecutor,
            IMemoryCache cache,
            IOptions<BlobConfigurationOptions> options,
            IOptions<PatientMatchOptions> matchOptions,
            IOptions<DatabaseConnectionOptions> connections)
        {
            _connectionFactory = connectionFactory;
            _commandExecutor = commandExecutor;
            _cache = cache;
            _cacheDuration = options.Value.CacheDuration;
            _matchOptions = matchOptions.Value;
            _connections = connections.Value;
        }

        public async Task<IReadOnlyList<PatientCandidate>> FindCandidatesAsync(
            DatabaseContext database,
            NormalizedPatientIdentity requested,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(requested);

            var roleId = await GetPatientRoleIdAsync(database.Environment, cancellationToken).ConfigureAwait(false);

            // REMS database: ConnectionStrings:RemsDb from appsettings.
            var remsDatabase = new ResolvedDatabase(
                database.Organization,
                database.Environment,
                Required(_connections.RemsDb, "ConnectionStrings:RemsDb"));

            await using var remsConnection = await _connectionFactory
                .CreateOpenAsync(remsDatabase, cancellationToken)
                .ConfigureAwait(false);

            // One query does steps 1-4 for the stored records. The request values arrive already validated
            // and normalized by C# (PatientLookupValidator). See the comments in the SQL.
            return await _commandExecutor.ExecuteReaderAsync(
                remsConnection,
                """
                -- =====================================================================================
                -- REMS-to-REMS patient lookup (API 1): find REMS patients who may be the requested person.
                --
                -- Parameters (request values are already validated and normalized by C#):
                --   @UserRoleID                   ID of the "REMS Patient" role (from the Survey database)
                --   @DobQuestionName, @ZipQuestionName   Question.Name of the DOB and ZIP answers
                --   @DateOfBirth                  requested DOB, a UTC date (e.g. 1979-12-31)
                --   @FirstName, @LastName         normalized, e.g. JOHN, SMITH
                --   @ZipCode                      exactly 5 digits
                --   @ShortNameLength              3: names this short also need the 1-edit check
                --   @TriggerEnrolmentStatuses     comma-separated statuses that allow disclosure ("Enrolled")
                --   @ReviewThresholdExactMatches  2: this many exact matches are held for review
                --
                -- Where the data lives:
                --   [User]          one row per person: name and role
                --   FormResponse    one row per enrollment form a patient filled in, with its EnrolmentStatus
                --   FormAnswer      one row per answer on a form; DOB and ZIP are answers, not [User] columns
                --   Question        the question text (Name) each answer belongs to
                --
                -- Steps (each CTE reads the one before it; SQL Server runs them as one plan):
                --   PatientEnrollment  newest enrollment form per patient that has a DOB answer
                --   Candidate          read DOB and ZIP answers; keep only the exact DOB (most rows go here)
                --   Normalized         clean stored names and ZIP the same way C# cleans the request
                --   Compared           score each field: 2 = identical, 1 = similar, 0 = differs
                --   Classified         tier: 2 = exact, 1 = partial, 0 = none
                --   Triggered          drop tier none; check the enrolment status (trigger)
                --   final SELECT       release decision for every remaining row
                --
                -- Result: one row per exact or partial candidate, with its field scores, tier and decision.
                -- Tier-none records never leave the database.
                -- =====================================================================================

                -- PatientEnrollment
                --   In:   every FormResponse of a user with the "REMS Patient" role.
                --   Does: keeps only forms that have a DOB answer, and numbers each patient's forms newest
                --         first (RowNumber 1 = newest; ID breaks a tie on CreatedDate).
                --   Out:  one row per form: form ID, patient, enrolment status, RowNumber.
                WITH PatientEnrollment AS (
                    SELECT fr.[ID], fr.[UserID], fr.[EnrolmentStatusID],
                           ROW_NUMBER() OVER (PARTITION BY fr.[UserID] ORDER BY fr.[CreatedDate] DESC, fr.[ID] DESC) AS [RowNumber]
                    FROM [dbo].[FormResponse] fr
                    INNER JOIN [dbo].[User] u ON u.[ID] = fr.[UserID] AND u.[UserRoleID] = @UserRoleID
                    WHERE EXISTS (
                        SELECT 1
                        FROM [dbo].[FormAnswer] fa
                        INNER JOIN [dbo].[Question] q ON q.[ID] = fa.[QuestionID]
                        WHERE fa.[FormResponseID] = fr.[ID] AND q.[Name] = @DobQuestionName)
                ),

                -- Candidate (DOB gate)
                --   In:   PatientEnrollment.
                --   Does: for each form, CROSS APPLY looks up two answers on that form by question name:
                --         DobAnswer ('DOB') and ZipAnswer ('Zip Code'), trimmed. If a question was answered
                --         twice on the form, the latest answer wins (CreatedDate, then ID).
                --         Then keeps only each patient's newest form (RowNumber = 1) whose DOB, read as a date,
                --         equals @DateOfBirth exactly. The stored DOB is text, read as:
                --           style 23 on the first 10 characters: '1979-12-31T18:30:00.000Z' -> 1979-12-31
                --                                                   (the UTC date; the time is ignored)
                --           style 101 if that fails:              '01/31/1980' -> 1980-01-31
                --         TRY_CONVERT gives NULL for text that is not a date, so such a patient never matches.
                --   Out:  patients with the requested DOB: patient, enrolment status, raw ZIP answer.
                --         Usually only a few rows are left after this step.
                Candidate AS (
                    SELECT pe.[UserID], pe.[EnrolmentStatusID], answers.[ZipAnswer]
                    FROM PatientEnrollment pe
                    CROSS APPLY (
                        SELECT
                            (SELECT TOP (1) LTRIM(RTRIM(fa.[QuestionAnswer]))
                             FROM [dbo].[FormAnswer] fa
                             INNER JOIN [dbo].[Question] q ON q.[ID] = fa.[QuestionID]
                             WHERE fa.[FormResponseID] = pe.[ID] AND q.[Name] = @DobQuestionName
                             ORDER BY fa.[CreatedDate] DESC, fa.[ID] DESC) AS [DobAnswer],
                            (SELECT TOP (1) LTRIM(RTRIM(fa.[QuestionAnswer]))
                             FROM [dbo].[FormAnswer] fa
                             INNER JOIN [dbo].[Question] q ON q.[ID] = fa.[QuestionID]
                             WHERE fa.[FormResponseID] = pe.[ID] AND q.[Name] = @ZipQuestionName
                             ORDER BY fa.[CreatedDate] DESC, fa.[ID] DESC) AS [ZipAnswer]
                    ) answers
                    WHERE pe.[RowNumber] = 1
                      AND COALESCE(
                              TRY_CONVERT(date, LEFT(answers.[DobAnswer], 10), 23),
                              TRY_CONVERT(date, answers.[DobAnswer], 101)) = @DateOfBirth
                ),

                -- Normalized (step 1 for the stored record)
                --   In:   Candidate, joined to [User] for the stored first and last name.
                --   Does: cleans the stored names with the same rules C# applies to the request
                --         (PatientNameNormalizer), so both sides can be compared directly:
                --           "Dr. Mary-Ann Lou" -> MARYANN      "Van Der Berg Jr." -> VANDERBERG
                --           "O'Brien" / "O’Brien" -> OBRIEN   "Mr John" -> JOHN
                --         and cuts the stored ZIP to five digits. Each CROSS APPLY below is one small step.
                --         A record whose stored name has characters outside the allowed set is left out:
                --         it cannot be compared fairly. Stored names are not accent-folded yet (C# folds the
                --         request: José -> JOSE), so a stored "José" is also left out.
                --   Out:  patient, enrolment status, normalized FirstName, LastName and ZipCode.
                Normalized AS (
                    SELECT c.[UserID], c.[EnrolmentStatusID], u.[Username], u.[EmailID], u.[CreatedBy], u.[CreatedDate],
                           u.[UpdatedBy], u.[UpdatedDate], u.[Status], fn.[Name] AS [FirstName], ln.[Name] AS [LastName],
                           -- ZIP: "12345", "12345-6789" or "123456789" -> "12345"; any other format -> '' (never matches).
                           CASE WHEN c.[ZipAnswer] LIKE '[0-9][0-9][0-9][0-9][0-9]'
                                  OR c.[ZipAnswer] LIKE '[0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]'
                                  OR c.[ZipAnswer] LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
                                THEN LEFT(c.[ZipAnswer], 5) ELSE '' END AS [ZipCode]
                    FROM Candidate c
                    INNER JOIN [dbo].[User] u ON u.[ID] = c.[UserID]
                    -- t1: uppercase; "," becomes a space (word break); ".", apostrophes (' and the curly ’,
                    --     NCHAR(8217)) and hyphens are removed (joined); outer spaces trimmed.
                    --     "Dr. Mary-Ann Lou" -> "DR MARYANN LOU"      "Smith, Jr." -> "SMITH  JR"      "J.R." -> "JR"
                    CROSS APPLY (
                        SELECT LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(u.[FirstName]), '.', ''), ',', ' '), '''', ''), NCHAR(8217), ''), '-', ''))) AS [F],
                               LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(u.[LastName]), '.', ''), ',', ' '), '''', ''), NCHAR(8217), ''), '-', ''))) AS [L]
                    ) t1
                    -- t:  runs of spaces become one, so words are separated by exactly one space:
                    --     "SMITH  JR" -> "SMITH JR". Each space becomes "<>", "><" pairs are removed, "<>" turns
                    --     back into one space. "<" and ">" are not allowed in a name, so they cannot clash.
                    CROSS APPLY (
                        SELECT REPLACE(REPLACE(REPLACE(t1.[F], ' ', '<>'), '><', ''), '<>', ' ') AS [F0],
                               REPLACE(REPLACE(REPLACE(t1.[L], ' ', '<>'), '><', ''), '<>', ' ') AS [L0]
                    ) t
                    -- f:  first name, remove titles. Every word is wrapped in its own pair of spaces
                    --     ("DR MRS JANE" -> " DR  MRS  JANE "), so REPLACE(' MR ', ' ') removes the whole word MR
                    --     but never part of another word (MRS, MARY). All title words are removed:
                    --     " DR  MRS  JANE " -> "JANE". Titles: MR, MRS, MS, MISS, MX, DR, PROF.
                    CROSS APPLY (
                        SELECT LTRIM(RTRIM(
                                   REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                       ' ' + REPLACE(t.[F0], ' ', '  ') + ' ',
                                       ' MR ', ' '), ' MRS ', ' '), ' MS ', ' '), ' MISS ', ' '), ' MX ', ' '), ' DR ', ' '), ' PROF ', ' ')
                               )) AS [Rest]
                    ) f
                    -- fn: the normalized first name = the first word left after removing titles
                    --     ("MARYANN LOU" -> MARYANN). A name of titles only ("DR") gives '' and the record is
                    --     left out below, as C# rejects such a request name. Same result as the C# rule
                    --     "drop leading titles, then take the first word".
                    CROSS APPLY (SELECT LEFT(f.[Rest] + ' ', CHARINDEX(' ', f.[Rest] + ' ') - 1) AS [Name]) fn
                    -- l0: last name, mark the end. Words are wrapped in their own spaces and "|" is added after
                    --     the last word: "VAN DER BERG JR" -> " VAN  DER  BERG  JR |".
                    CROSS APPLY (SELECT ' ' + REPLACE(t.[L0], ' ', '  ') + ' |' AS [S0]) l0
                    -- l:  remove trailing suffixes. ' JR |' -> '|' removes JR only when it is the last word;
                    --     a JR in the middle is kept ("SMITH JR SMITH" -> SMITHJRSMITH). After one is removed
                    --     the word before it is last, so the six suffixes are tried in three rounds
                    --     ("SMITH JR III" -> "SMITH |"). Then "|" and the spaces are removed, joining the words:
                    --     " VAN  DER  BERG |" -> VANDERBERG. Suffixes: JR, SR, II, III, IV, V.
                    --     Three rounds always remove at least three trailing suffixes; C# removes any number.
                    CROSS APPLY (
                        SELECT REPLACE(REPLACE(
                                   REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                   REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                   REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                       l0.[S0],
                                       ' JR |', '|'), ' SR |', '|'), ' II |', '|'), ' III |', '|'), ' IV |', '|'), ' V |', '|'),
                                       ' JR |', '|'), ' SR |', '|'), ' II |', '|'), ' III |', '|'), ' IV |', '|'), ' V |', '|'),
                                       ' JR |', '|'), ' SR |', '|'), ' II |', '|'), ' III |', '|'), ' IV |', '|'), ' V |', '|'),
                                   '|', ''), ' ', '') AS [Rest]
                    ) l
                    -- ln: the normalized last name. If the name was only suffixes (nothing left), keep its first
                    --     word ("JR" -> JR, "JR II" -> JR), the same as C#.
                    CROSS APPLY (SELECT IIF(l.[Rest] = '', LEFT(t.[L0] + ' ', CHARINDEX(' ', t.[L0] + ' ') - 1), l.[Rest]) AS [Name]) ln
                    -- Keep only records whose stored names use the allowed characters, A-Z a-z space . , ' ’ -
                    -- (the set the C# validator allows after folding the request), and are not empty after cleaning.
                    -- Binary collation compares exact characters, so é is not treated as e.
                    -- The hyphen must be first in [^...]: elsewhere LIKE reads it as a range ("'-]" would also
                    -- allow digits and ( ) * + / : ; < = > ? @ [ \ ]).
                    WHERE u.[FirstName] COLLATE Latin1_General_BIN2 NOT LIKE N'%[^-A-Za-z .,''' + NCHAR(8217) + N']%'
                      AND u.[LastName] COLLATE Latin1_General_BIN2 NOT LIKE N'%[^-A-Za-z .,''' + NCHAR(8217) + N']%'
                      AND fn.[Name] <> ''
                      AND ln.[Name] <> ''
                ),

                -- Compared (step 2)
                --   In:   Normalized.
                --   Does: scores each field against the request:
                --           FirstName, LastName  2 = identical
                --                                1 = similar: same SOUNDEX code (sounds alike: SMITH/SMYTH = S530);
                --                                    if either name has 3 letters or fewer, they must also be at
                --                                    most 1 edit apart (JON/JOHN yes; IAN/INA no, 2 edits)
                --                                0 = differs
                --           ZipCode              2 = identical, 0 = differs (no "similar" for ZIP)
                --   Out:  Normalized plus FirstNameMatch, LastNameMatch, ZipMatch.
                Compared AS (
                    SELECT n.*,
                           CASE WHEN n.[FirstName] = @FirstName THEN 2
                                WHEN SOUNDEX(n.[FirstName]) = SOUNDEX(@FirstName)
                                     AND ((LEN(n.[FirstName]) > @ShortNameLength AND LEN(@FirstName) > @ShortNameLength)
                                          OR fe.[WithinOneEdit] = 1) THEN 1
                                ELSE 0 END AS [FirstNameMatch],
                           CASE WHEN n.[LastName] = @LastName THEN 2
                                WHEN SOUNDEX(n.[LastName]) = SOUNDEX(@LastName)
                                     AND ((LEN(n.[LastName]) > @ShortNameLength AND LEN(@LastName) > @ShortNameLength)
                                          OR le.[WithinOneEdit] = 1) THEN 1
                                ELSE 0 END AS [LastNameMatch],
                           CASE WHEN n.[ZipCode] = @ZipCode THEN 2 ELSE 0 END AS [ZipMatch]
                    FROM Normalized n
                    -- fp/fe (first name) and lp/le (last name): the "at most 1 edit apart" check, written by hand
                    -- because EDIT_DISTANCE exists only in Azure SQL and SQL Server 2025 (this server is 2019).
                    --   fp/lp: which name is longer and which is shorter.
                    --   fe/le: WithinOneEdit = 1 when
                    --          same length and at most one letter differs            (ANN / ANE)
                    --          or one letter longer, and deleting one of its letters
                    --          gives the shorter name                                  (JOHN -> JON)
                    --   The result is only used when a name has 3 letters or fewer, so the other name is at most
                    --   4 letters when they are 1 edit apart: checking positions 1-4 is enough.
                    CROSS APPLY (
                        SELECT IIF(LEN(n.[FirstName]) >= LEN(@FirstName), n.[FirstName], @FirstName) AS [Longer],
                               IIF(LEN(n.[FirstName]) >= LEN(@FirstName), @FirstName, n.[FirstName]) AS [Shorter]
                    ) fp
                    CROSS APPLY (
                        SELECT CASE
                                   WHEN LEN(fp.[Longer]) = LEN(fp.[Shorter])
                                        AND (SELECT COUNT(*) FROM (VALUES (1), (2), (3), (4)) p(i)
                                             WHERE SUBSTRING(fp.[Longer], p.i, 1) <> SUBSTRING(fp.[Shorter], p.i, 1)) <= 1 THEN 1
                                   WHEN LEN(fp.[Longer]) = LEN(fp.[Shorter]) + 1
                                        AND EXISTS (SELECT 1 FROM (VALUES (1), (2), (3), (4)) p(i)
                                                    WHERE STUFF(fp.[Longer], p.i, 1, '') = fp.[Shorter]) THEN 1
                                   ELSE 0 END AS [WithinOneEdit]
                    ) fe
                    CROSS APPLY (
                        SELECT IIF(LEN(n.[LastName]) >= LEN(@LastName), n.[LastName], @LastName) AS [Longer],
                               IIF(LEN(n.[LastName]) >= LEN(@LastName), @LastName, n.[LastName]) AS [Shorter]
                    ) lp
                    CROSS APPLY (
                        SELECT CASE
                                   WHEN LEN(lp.[Longer]) = LEN(lp.[Shorter])
                                        AND (SELECT COUNT(*) FROM (VALUES (1), (2), (3), (4)) p(i)
                                             WHERE SUBSTRING(lp.[Longer], p.i, 1) <> SUBSTRING(lp.[Shorter], p.i, 1)) <= 1 THEN 1
                                   WHEN LEN(lp.[Longer]) = LEN(lp.[Shorter]) + 1
                                        AND EXISTS (SELECT 1 FROM (VALUES (1), (2), (3), (4)) p(i)
                                                    WHERE STUFF(lp.[Longer], p.i, 1, '') = lp.[Shorter]) THEN 1
                                   ELSE 0 END AS [WithinOneEdit]
                    ) le
                ),

                -- Classified (step 3)
                --   In:   Compared.
                --   Does: counts how many of first name, last name and ZIP match (identical or similar):
                --           3, all identical      -> 2 exact
                --           3 with a similar name -> 1 partial
                --           2                     -> 1 partial
                --           0 or 1                -> 0 none
                --         (The DOB already matched in Candidate; it is the gate, not one of the three.)
                --   Out:  Compared plus Tier.
                Classified AS (
                    SELECT c.*,
                           CASE WHEN c.[FirstNameMatch] = 2 AND c.[LastNameMatch] = 2 AND c.[ZipMatch] = 2 THEN 2
                                WHEN IIF(c.[FirstNameMatch] > 0, 1, 0) + IIF(c.[LastNameMatch] > 0, 1, 0)
                                     + IIF(c.[ZipMatch] > 0, 1, 0) >= 2 THEN 1
                                ELSE 0 END AS [Tier]
                    FROM Compared c
                ),

                -- Triggered (step 4, trigger check)
                --   In:   Classified, joined to EnrolmentStatus for the status name of the newest form.
                --   Does: drops tier none (it is never disclosed, so it never leaves the database), then sets
                --         Triggered = 1 when the status is one of @TriggerEnrolmentStatuses (split on commas,
                --         trimmed; e.g. "Enrolled"). No trigger, no disclosure. The enrolment status stands in
                --         for the real trigger rules (dose within 90 / 14 days, safety event) until that data exists.
                --   Out:  exact and partial candidates with EnrolmentStatus and Triggered.
                Triggered AS (
                    SELECT c.*, es.[Name] AS [EnrolmentStatus],
                           CASE WHEN EXISTS (
                                    SELECT 1
                                    FROM STRING_SPLIT(@TriggerEnrolmentStatuses, ',') s
                                    WHERE LTRIM(RTRIM(s.[value])) <> '' AND LTRIM(RTRIM(s.[value])) = LTRIM(RTRIM(es.[Name])))
                                THEN 1 ELSE 0 END AS [Triggered]
                    FROM Classified c
                    LEFT JOIN [dbo].[EnrolmentStatus] es ON es.[ID] = c.[EnrolmentStatusID]
                    WHERE c.[Tier] > 0
                )

                -- Final SELECT (step 4, release decision)
                --   In:   Triggered.
                --   Does: decides each row; the values match CandidateDecision in C#:
                --           1 withheld          not triggered (never disclosed)
                --           2 held for review   when @ReviewThresholdExactMatches (2) or more rows are exact AND
                --                               triggered, every triggered row is held for internal review
                --           3 released          otherwise
                --         SUM(...) OVER () counts the exact, triggered rows across the whole result, so every row
                --         sees the same total. A threshold of 0 turns the review off.
                --   Out:  the result read by ReadCandidatesAsync; C# returns only the released rows.
                SELECT t.[UserID] AS [ID], t.[FirstName], t.[LastName], t.[ZipCode], t.[Username], t.[EmailID],
                       t.[CreatedBy], t.[CreatedDate], t.[UpdatedBy], t.[UpdatedDate], t.[Status],
                       t.[FirstNameMatch], t.[LastNameMatch], t.[ZipMatch], t.[Tier], t.[EnrolmentStatus], t.[Triggered],
                       CASE WHEN t.[Triggered] = 0 THEN 1
                            WHEN @ReviewThresholdExactMatches > 0
                                 AND SUM(IIF(t.[Triggered] = 1 AND t.[Tier] = 2, 1, 0)) OVER () >= @ReviewThresholdExactMatches THEN 2
                            ELSE 3 END AS [Decision]
                FROM Triggered t;
                """,
                reader => ReadCandidatesAsync(reader, requested.DateOfBirth, cancellationToken),
                cancellationToken,
                ("@UserRoleID", roleId),
                ("@DobQuestionName", _matchOptions.DobQuestionName),
                ("@ZipQuestionName", _matchOptions.ZipQuestionName),
                ("@DateOfBirth", requested.DateOfBirth.ToDateTime(TimeOnly.MinValue)),
                ("@FirstName", requested.FirstName),
                ("@LastName", requested.LastName),
                ("@ZipCode", requested.ZipCode),
                ("@ShortNameLength", PatientMatcher.ShortNameLength),
                ("@TriggerEnrolmentStatuses", string.Join(",", _matchOptions.TriggerEnrolmentStatuses)),
                ("@ReviewThresholdExactMatches", _matchOptions.ReviewThresholdExactMatches)).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<PatientCandidate>> ReadCandidatesAsync(
            DbDataReader reader,
            DateOnly dateOfBirth,
            CancellationToken cancellationToken)
        {
            var candidates = new List<PatientCandidate>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(new PatientCandidate
                {
                    UserId = Convert.ToInt32(reader["ID"]),
                    FirstName = (string)reader["FirstName"],
                    LastName = (string)reader["LastName"],
                    // The DOB gate is exact, so every candidate has the requested DOB.
                    DateOfBirth = dateOfBirth,
                    ZipCode = (string)reader["ZipCode"],
                    FirstNameMatch = (FieldMatchResult)Convert.ToInt32(reader["FirstNameMatch"]),
                    LastNameMatch = (FieldMatchResult)Convert.ToInt32(reader["LastNameMatch"]),
                    ZipMatch = (FieldMatchResult)Convert.ToInt32(reader["ZipMatch"]),
                    Tier = (PatientMatchTier)Convert.ToInt32(reader["Tier"]),
                    EnrolmentStatus = reader["EnrolmentStatus"] as string,
                    Triggered = Convert.ToInt32(reader["Triggered"]) == 1,
                    Decision = (CandidateDecision)Convert.ToInt32(reader["Decision"]),
                    Username = reader["Username"] as string ?? string.Empty,
                    EmailID = reader["EmailID"] as string ?? string.Empty,
                    CreatedBy = Convert.ToInt32(reader["CreatedBy"]),
                    CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                    UpdatedBy = Convert.ToInt32(reader["UpdatedBy"]),
                    UpdatedDate = Convert.ToDateTime(reader["UpdatedDate"]),
                    Status = Convert.ToInt32(reader["Status"])
                });
            }

            return candidates;
        }

        private static string Required(string? connectionString, string name) =>
            string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException($"{name} is not set in appsettings.")
                : connectionString;

        // The role ID is fixed per Survey environment; cache it to avoid a Survey round trip per lookup.
        private async Task<int> GetPatientRoleIdAsync(
            string environment,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"role-id:{environment.Trim()}:{UserRoleName}".ToUpperInvariant();
            if (_cache.TryGetValue(cacheKey, out int cachedRoleId))
            {
                return cachedRoleId;
            }

            // Survey database: ConnectionStrings:SurveyDb from appsettings.
            var surveyDatabase = new ResolvedDatabase(
                "SURVEY",
                environment,
                Required(_connections.SurveyDb, "ConnectionStrings:SurveyDb"));

            await using var surveyConnection = await _connectionFactory
                .CreateOpenAsync(surveyDatabase, cancellationToken)
                .ConfigureAwait(false);

            var roleValue = await _commandExecutor.ExecuteScalarAsync(
                surveyConnection,
                """
                SELECT TOP (1) [ID]
                FROM [dbo].[UserRole]
                WHERE [Name] = @RoleName
                  AND [Status] = 1;
                """,
                cancellationToken,
                ("@RoleName", UserRoleName)).ConfigureAwait(false);

            if (roleValue is null || roleValue is DBNull)
            {
                throw new InvalidOperationException(
                    $"The Survey UserRole '{UserRoleName}' was not found.");
            }

            var roleId = Convert.ToInt32(roleValue);
            _cache.Set(cacheKey, roleId, _cacheDuration);
            return roleId;
        }
    }
}

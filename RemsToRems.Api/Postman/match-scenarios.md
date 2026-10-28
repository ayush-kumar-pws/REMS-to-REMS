# Patient match scenarios: test data, payloads and expected results

`POST /registry/match` · `Content-Type: application/json` · names, DOB and ZIP go in a `patient` object (see the payload template at the end); the tables list them flat for readability.

Every expected result below was checked by running the production lookup query on these patients (41/41 pass).

## 1. Create these patients (normal enrollment form)

Enter the **form DOB** (the real birthday). The form stores it as UTC, one day earlier, so every request sends the **request DOB**.

| ID | First name | Last name | Form DOB | Request DOB | ZIP | Status |
|---|---|---|---|---|---|---|
| P1 | Robert | Johnson | 1985-01-01 | 1984-12-31 | 10001 | Enrolled |
| P2 | Amy | Lee | 1985-02-01 | 1985-01-31 | 20002 | Enrolled |
| P3 | Dr. William | Smith Jr. | 1985-03-01 | 1985-02-28 | 30003 | Enrolled |
| P4 | Mary-Kate | O'Neil | 1985-04-01 | 1985-03-31 | 40004 | Enrolled |
| P5 | Thomas | Clark | 1985-05-01 | 1985-04-30 | 50005 | Pending |
| P6 | Thomas | Clark | 1985-06-01 | 1985-05-31 | 60006 | Deactivated |
| P7 | Daniel | Wright | 1985-07-01 | 1985-06-30 | 70007 | Enrolled |
| P8 | Daniel | Wright | 1985-07-01 | 1985-06-30 | 70007 | Enrolled |
| P9 | Daniel | Wright | 1985-08-01 | 1985-07-31 | 80008 | Enrolled |
| P10 | Daniel | Wright | 1985-08-01 | 1985-07-31 | 80008 | Pending |
| P11 | Kevin | Brown | 1985-09-01 | 1985-08-31 | 90009 | Enrolled |
| P12 | Kevin | Brown | 1985-09-01 | 1985-08-31 | 90009 | Enrolled |
| P13 | Kevan | Brown | 1985-09-01 | 1985-08-31 | 90009 | Enrolled |
| P14 | Paul | Adams | 1985-10-01 | 1985-09-30 | 12345-6789 | Enrolled |
| P15 | Nancy | Drew | 1985-11-01 | 1985-10-31 | 11011 | Unenrolled |

P14 needs the form to accept a ZIP+4. If it doesn't, skip P14 and R32.

## 2. Response shape

Every request returns HTTP 200. Use a new GUID for each `requestId` (in Postman: `"{{$guid}}"`).

```json
{
  "requestId": "<the requestId you sent>",
  "responseId": "<new GUID>",
  "asOf": "<UTC time, e.g. 2026-10-27T10:15:02Z>",
  "patients": [
    { "remsId": "<32 hex characters>", "firstName": "ROBERT", "lastName": "JOHNSON",
      "dateOfBirth": "1984-12-31", "zipcode": "10001", "matchTier": "EXACT" }
  ]
}
```

- `patients` lists released matches only. `[]` means no match, no trigger, or held for review.
- Names in the response are the **stored** patient's normalized name, not what you sent.
- `dateOfBirth` is the request DOB; `zipcode` is the stored ZIP cut to 5 digits.
- With several patients, the order is not fixed: check by `matchTier`.

Field results used below: **identical** (exact), **similar** (same Soundex; names of 3 letters or fewer also need at most 1 edit), **differs**.

## 3. Long names (4+ letters): Soundex — patient P1 ROBERT JOHNSON

| ID | firstName | lastName | dateOfBirth | zipcode | Fields (first / last / ZIP) | Expected `patients` |
|---|---|---|---|---|---|---|
| R1 | Robert | Johnson | 1984-12-31 | 10001 | identical / identical / identical | ROBERT JOHNSON **EXACT** |
| R2 | Rupert | Johnson | 1984-12-31 | 10001 | similar (R163) / identical / identical | ROBERT JOHNSON **PARTIAL** |
| R3 | Robert | Jonson | 1984-12-31 | 10001 | identical / similar (J525) / identical | ROBERT JOHNSON **PARTIAL** |
| R4 | Rupert | Jonson | 1984-12-31 | 10001 | similar / similar / identical | ROBERT JOHNSON **PARTIAL** |
| R5 | Robert | Johnson | 1984-12-31 | 99999 | identical / identical / differs | ROBERT JOHNSON **PARTIAL** |
| R6 | Rupert | Jonson | 1984-12-31 | 99999 | similar / similar / differs | ROBERT JOHNSON **PARTIAL** |
| R7 | Robin | Johnson | 1984-12-31 | 10001 | differs (R150) / identical / identical | ROBERT JOHNSON **PARTIAL** |
| R8 | Robin | Johnston | 1984-12-31 | 10001 | differs / differs (J523) / identical | `[]` |
| R9 | Robert | Johnston | 1984-12-31 | 99999 | identical / differs / differs | `[]` |
| R10 | Rupert | Johnston | 1984-12-31 | 99999 | similar / differs / differs | `[]` |
| R11 | Mary | Jones | 1984-12-31 | 99999 | differs / differs / differs (DOB only) | `[]` |

## 4. Short names (3 letters or fewer): Soundex + edit distance — patient P2 AMY LEE

| ID | firstName | lastName | dateOfBirth | zipcode | Fields (first / last / ZIP) | Expected `patients` |
|---|---|---|---|---|---|---|
| R12 | Amy | Lee | 1985-01-31 | 20002 | identical / identical / identical | AMY LEE **EXACT** |
| R13 | Ami | Lee | 1985-01-31 | 20002 | similar (A500, 1 edit) / identical / identical | AMY LEE **PARTIAL** |
| R14 | Am | Lee | 1985-01-31 | 20002 | similar (A500, 1 edit: delete Y) / identical / identical | AMY LEE **PARTIAL** |
| R15 | Amie | Lee | 1985-01-31 | 20002 | differs (A500 but 2 edits) / identical / identical | AMY LEE **PARTIAL** |
| R16 | Amie | Lee | 1985-01-31 | 99999 | differs (2 edits) / identical / differs | `[]` |
| R17 | Ami | Lee | 1985-01-31 | 99999 | similar / identical / differs | AMY LEE **PARTIAL** |
| R18 | Amy | Lea | 1985-01-31 | 20002 | identical / similar (L000, 1 edit) / identical | AMY LEE **PARTIAL** |
| R19 | Amy | Le | 1985-01-31 | 20002 | identical / similar (L000, 1 edit) / identical | AMY LEE **PARTIAL** |
| R20 | Amy | Li | 1985-01-31 | 20002 | identical / differs (L000 but 2 edits) / identical | AMY LEE **PARTIAL** |
| R21 | Amy | Li | 1985-01-31 | 99999 | identical / differs (2 edits) / differs | `[]` |
| R22 | Ami | Lea | 1985-01-31 | 99999 | similar / similar / differs | AMY LEE **PARTIAL** |
| R23 | Emy | Lee | 1985-01-31 | 99999 | differs (E500, other first letter) / identical / differs | `[]` |
| R24 | Amy | Leigh | 1985-01-31 | 20002 | identical / differs (L200) / identical | AMY LEE **PARTIAL** |

## 5. Normalization (titles, suffixes, punctuation, ZIP)

| ID | firstName | lastName | dateOfBirth | zipcode | What it tests | Expected `patients` |
|---|---|---|---|---|---|---|
| R25 | William | Smith | 1985-02-28 | 30003 | stored "Dr. William" / "Smith Jr." → WILLIAM / SMITH | WILLIAM SMITH **EXACT** |
| R26 | Dr. Mr. William | Smith Jr. III | 1985-02-28 | 30003 | all titles and suffixes dropped on both sides | WILLIAM SMITH **EXACT** |
| R27 | Bill | Smith | 1985-02-28 | 30003 | nickname: no nickname check, BILL differs | WILLIAM SMITH **PARTIAL** |
| R28 | Mary-Kate | O'Neil | 1985-03-31 | 40004 | hyphen and apostrophe removed | MARYKATE ONEIL **EXACT** |
| R29 | Marykate | ONeil | 1985-03-31 | 40004 | same name without punctuation | MARYKATE ONEIL **EXACT** |
| R30 | Mary-Kate | O’Neil | 1985-03-31 | 40004 | curly apostrophe ’ = straight ' | MARYKATE ONEIL **EXACT** |
| R31 | Mary Kate | O'Neil | 1985-03-31 | 40004 | two words: only MARY kept, differs from MARYKATE | MARYKATE ONEIL **PARTIAL** |
| R32 | Paul | Adams | 1985-09-30 | 12345 | stored ZIP+4 12345-6789 cut to 12345 | PAUL ADAMS **EXACT** |

## 6. Release: trigger (enrolment status) and review

| ID | firstName | lastName | dateOfBirth | zipcode | What happens | Expected `patients` |
|---|---|---|---|---|---|---|
| R33 | Thomas | Clark | 1985-04-30 | 50005 | P5 exact, but Pending → withheld | `[]` |
| R34 | Thomas | Clark | 1985-05-31 | 60006 | P6 exact, but Deactivated → withheld | `[]` |
| R35 | Nancy | Drew | 1985-10-31 | 11011 | P15 exact, but Unenrolled → withheld | `[]` |
| R36 | Daniel | Wright | 1985-06-30 | 70007 | P7 + P8 both exact and Enrolled → held for review | `[]` |
| R37 | Danial | Wright | 1985-06-30 | 70007 | P7 + P8 only similar (D540); partials don't trigger review | DANIEL WRIGHT **PARTIAL**, DANIEL WRIGHT **PARTIAL** |
| R38 | Daniel | Wright | 1985-07-31 | 80008 | P9 exact Enrolled + P10 exact Pending: only 1 triggered exact | DANIEL WRIGHT **EXACT** (P9 only) |
| R39 | Kevin | Brown | 1985-08-31 | 90009 | P11 + P12 exact, P13 partial (KEVAN K150) → all held | `[]` |
| R40 | Kevan | Brown | 1985-08-31 | 90009 | P13 exact, P11 + P12 similar: only 1 exact → all released | KEVAN BROWN **EXACT**, KEVIN BROWN **PARTIAL**, KEVIN BROWN **PARTIAL** |

## 7. DOB gate

| ID | firstName | lastName | dateOfBirth | zipcode | What happens | Expected `patients` |
|---|---|---|---|---|---|---|
| R41 | Robert | Johnson | 1985-01-01 | 10001 | real birthday sent instead of the stored UTC date (1984-12-31) | `[]` |

## 8. Validation errors (no data needed)

HTTP 400, `Content-Type: application/problem+json`. No lookup runs and no `patients` are returned. Body:

```json
{
  "type": "urn:rems-exchange:problem:validation-error",
  "title": "The request is not valid",
  "status": 400,
  "detail": "2 fields failed validation. No lookup was performed.",
  "instance": "/registry/match",
  "requestId": "3de6e667-9c77-4ec3-b18e-cdfcc19cae4c",
  "responseId": "66cc69be-fab3-4b74-853d-da5fc5b1bf7e",
  "errors": [
    { "field": "dateOfBirth", "reason": "missing", "code": 109 },
    { "field": "zipcode", "reason": "not exactly 5 digits", "code": 110 }
  ]
}
```

`requestId` is echoed only when it was a valid UUID. Each field has at most one error.

| ID | Payload change | `errors` (field, code) |
|---|---|---|
| V1 | no `requestId` (or `""`, `null`, all zeros) | requestId 101 |
| V2 | `"requestId": "abc"` | requestId 100 |
| V3 | no `dateOfBirth` | dateOfBirth 109 |
| V4 | `"dateOfBirth": "01/01/1980"` | dateOfBirth 108 |
| V5 | `"dateOfBirth": "1980-02-30"` | dateOfBirth 108 |
| V6 | `"dateOfBirth": "2999-01-01"` | dateOfBirth 108 (in the future; code pending partner confirmation) |
| V7 | no `zipcode` | zipcode 111 |
| V8 | `"zipcode": "1234"` | zipcode 110 |
| V9 | `"zipcode": "12345-6789"` | zipcode 110 |
| V10 | `"zipcode": "ABCDE"` | zipcode 110 |
| V11 | `"zipcode": 12345` (a number) | zipcode 110 |
| V12 | `"firstName": "J0hn"` or `"John@"` | firstName 112 |
| V13 | `"firstName": "Dr."` (only a title) | firstName 105 |
| V14 | `"firstName"` of 256 characters | firstName 104 |
| V15 | no `patient` object | firstName 105, lastName 107, dateOfBirth 109, zipcode 111 |
| V16 | `"firstName": "J0hn", "lastName": "Sm1th", "dateOfBirth": "01/01/1980", "zipcode": "1"` | firstName 112, lastName 112, dateOfBirth 108, zipcode 110 |
| V17 | body is not valid JSON | `errors: []`, detail says the body is not valid JSON |

Accepted, not errors: `José` (→ JOSE), `D‘Angelo` / `O’Brien` (curly quotes → '), `Mary–Ann` (en dash → -).

## Payload template

```json
{
  "requestId": "{{$guid}}",
  "patient": {
    "firstName": "Robert",
    "lastName": "Johnson",
    "dateOfBirth": "1984-12-31",
    "zipcode": "10001"
  }
}
```

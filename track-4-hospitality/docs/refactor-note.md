# Phase 8 refactor note: ChannelImportService

Scope: `backend/Larchmont.Api/Services/ChannelImportService.cs`, with characterisation tests in
`backend/Larchmont.Tests/ChannelImportCharacterisationTests.cs`. Behaviour was not meant to change.

## What was hard to follow

- One 230-line `ImportAsync` method with a single `foreach` body. Every rejection repeated the same
  three lines (`errors.Add(new ImportRowError(i + 1, extRef, "..."))`, `skipped++`, `continue`), about 16 times.
- Four counters (`total`, `imported`, `skipped`, `cancelled`) were updated in many places, so it was hard to see
  which branch changed which count. Cancellation counts as both `cancelled` and `imported`.
- The cancel branch, the MODIFIED branch and the new-booking branch were nested or interleaved, and
  `existing` was looked up before the status was known.
- Two CSV parsers were written out inline with almost the same loop. They differ in one way that matters:
  the header parser does not treat a doubled quote as an escape, the data-row parser does.
- Validation order was only visible by reading the code, and it is observable: the duplicate check sits
  between pricing and the email check.
- A local function `Get` closed over `cells` and `header`, which made the row state implicit.

## What changed (commit b90c3b0)

- `ParseHeader` and `SplitCells(line, escapedQuotes)` replace the two inline loops. The flag keeps the
  header and data-row behaviour different on purpose.
- `ImportRow` holds line number, header, and cells, with `Get`, `ExternalRef`, `Status`, `CellCount`.
- `ImportRun` holds the counters and errors. `Reject` and `Skip` give the one-line rejection pattern.
  `RecordImported` and `RecordCancelled` replace the repeated `imported++` and `cancelled++`.
- `ProcessRowAsync` runs the rules in the same order as before, using early `return`s instead of `continue`.
- Helpers: `CancelAsync`, `ApplyModificationAsync`, `ResolveTotalAsync` (returns a tuple, because async
  methods cannot take `out` parameters), `ReadStay`, `MapChannel`, `NewGuest`, `EnsureRequiredColumns`.
- Unchanged: public interface `IChannelImportService`, constructor signature, DI registration in
  `Program.cs`, every error message, every status code, and the `BusinessRuleException` for the empty
  file and missing columns.

Work was done in stages with a build and test run after each: parsing extraction, then the row pipeline
(one compile fix for `out` in async), then the column-check extraction. Only the final state is committed.

## How behaviour was proven the same

Commit order:

1. `36570da` Phase 8: characterisation tests. Added 115 test cases (76 existing tests still pass, total 191).
   These run against the unchanged service. All passed on the unchanged code before any refactor.
2. `b90c3b0` Phase 8: refactor. The characterisation file was not edited. 191/191 pass.
3. Mutation check (no commit). Three temporary mutations to the refactored code, each reverted afterwards:
   - M1: stay limit `> 30` changed to `>= 30`. Caught by `Stay_Of30Nights_IsAccepted` (1 failure).
   - M2: cancellation no longer clears `RoomId`. Caught by `Cancel_Confirmed_CancelsReleasesRoomAndNotesExistingChannel`.
   - M3: `Expedia` label changed to `Expedia Ltd`. Caught by 4 tests, including the sample-file test
     and a `MODIFIED` test.
   After reverting, the suite passed 191/191 again.

What the tests cover:

- Sample file `docs/sample-channel-export.csv`, run twice: on an empty database, and with two existing
  bookings seeded (one cancelled, one modified). Full result, error list, created bookings and notes.
- Each rejection message, and the order checks run in (dates before adults, occupancy before rate plan,
  rate plan before missing rate, duplicate after pricing, email before name, and so on).
- Header variants (case, spaces, quotes, column order, extra columns, BOM).
- Quoted commas, doubled quotes, cell trimming, short rows, blank and whitespace lines, CRLF.
- Cancellation, MODIFIED, and duplicate handling in each status state.
- Partial failure: a `SaveAsync` exception stops the file, and earlier rows stay committed.

## Bugs found, deliberately left alone

Each is pinned by a test that asserts the current behaviour. None were fixed.

1. **MODIFIED for an unknown booking creates a new booking.** There is no error. The row falls through to the
   new-booking path (`Modified_UnknownRef_CreatesNewBooking`).
2. **MODIFIED with no rate fails even when the booking exists.** Pricing runs before the existing-booking check
   (`Modified_WithNoRate_IsRejectedEvenWhenBookingExists`).
3. **Duplicate rows are judged inconsistently.** A duplicate with a bad date or no rate is reported as an error.
   A duplicate with a bad email or any unknown status is skipped silently
   (`DuplicateRef_ValidationErrorsAreStillReported`, `DuplicateRef_BadEmail_IsSilentlySkipped_...`).
4. **Length check uses the 11 required names, not the header width.** A row with 11 cells passes. Its missing
   `status` reads as `""`, which is not `CANCELLED`, so the row creates a booking
   (`ElevenColumnRow_PassesLengthCheck_...`).
5. **An unknown status is ignored for new rows.** `PENDING` or a blank status creates a confirmed booking.
6. **Email check accepts `@`.** Any string containing `@` passes (`GuestEmail_OnlyAtSign_IsAccepted`).
7. **Guest lookup is case-sensitive in code.** `ADA@x` does not match `ada@x` and creates a second guest.
   Real MySQL collation may hide this (`GuestEmail_IsMatchedCaseSensitively_CreatesDuplicateGuest`).
8. **No transaction.** Each row saves on its own. An exception stops the file, and earlier rows stay committed.
   A new guest is saved before the booking is, and a reference number is consumed before the save
   (`PartialFailure_EarlierRowsStayCommitted_LaterRowsNeverRun`).
9. **Service-level BOM breaks the header.** The controller's `StreamReader` strips the BOM for uploads, so the
   live path works. Direct callers get `Missing column 'external_ref'`
   (`Bom_BeforeFirstHeader_BreaksExternalRefColumn`).
10. **Header and data parsers disagree on doubled quotes.** The header does not unescape `""`. Data cells do.
11. **Trimming also removes spaces inside quotes.** `"  Ada  "` becomes `Ada` (`CellWhitespace_IsTrimmed_EvenInsideQuotes`).
12. **Rate-plan and total errors echo the input case.** The lookup uses upper case, the message does not.
    Not a bug on its own, but easy to misread.
13. **Cancellation skips the date and hotel checks.** An unknown booking is reported as "not held" before
    anything else, and a known one is cancelled even with bad dates
    (`Cancel_UnknownBooking_IsCheckedBeforeDatesAndHotel`, `Cancel_KnownBooking_IgnoresBadDates`).
14. **Duplicate lookup runs for every row that reaches it, before validation.** This is extra reads, not a
    correctness problem.

Not pinned (the lines are split before quote parsing): a quoted cell containing a newline, and a CR-only file.
Both are probably broken today.

## Limits of the proof

- Repositories are Moq mocks, not EF Core on MySQL. The tests show what the service calls and returns, not what
  the SQL does. Real behaviour may differ for collation (bug 7), unique constraints, `AddAsync` saving
  immediately, and `NextReferenceNumberAsync`.
- No HTTP-level test was added. `ImportsController` (multipart upload, BOM stripping, ProblemDetails) is not covered.
- Equivalence is shown by 115 characterisation cases plus three mutations. It is not a differential test
  against the old code on generated inputs. That would be the stronger proof and is a sensible follow-up.
- Timestamps use `DateTime.UtcNow` directly (no `IClock`). Tests check ranges, not exact values.
- Mock call ordering between repositories is only partly pinned. The sequence within each repo is checked
  where it matters (for example, no rate lookup for a supplied total).
- The mutation check covered 3 changes, not every branch.
- Tests live in `Larchmont.Tests`, which the backend scope marks as "only if the test agent asks". The task
  asked for them, so the new file was added and no existing test was edited.

<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Data & persistence

### Snapshot semantics for documents of record
AT requests freeze the client, case-manager, and (on select) vendor fields onto the
request row at creation, rather than reading them live at render. A payment request is a
document of record: it must re-render months later exactly as filed, even after the
client's name or the CM's phone changes. This is a deliberate departure from Sati's
compute-don't-store default (form due dates, upcoming events, compliance - all derived).
The live FK (`PersonId`, and the future `ProviderId`) rides alongside for
navigation/filtering but is *not* the render source.

**Rejected:** live lookup at render. It would silently rewrite filed financial documents
when source data changes.

### Rate, not amount, for every percentage
`PassthroughRate` (0.15) and `SalesTaxRate` (0.055) are stored as fractions the code
multiplies directly - no `/100` anywhere. `decimal(5,4)` columns give sub-percent room
(0.1550) without a schema change. Amounts get frozen onto the request at save; the rate
is the adjustable default.

### Passthrough applied post-tax
The 15% agency passthrough is computed on the *tax-inclusive* subtotal:
`(subtotal + tax) * (1 + rate)`. This is contrary to the OADS form's visual row order
(passthrough line sits above the tax line) but matches how the fee is actually assessed.
`ATRequestCalculator` is the single owner of this arithmetic.

### Mirrored math in the AT queue projection
`ATRequestService.GetAllForUserAsync` re-expresses the total formula inline in the LINQ
projection because EF can't translate `ATRequestCalculator.Total` into SQL. This is a
known, commented shadow copy: if the passthrough formula changes, it changes in the
calculator AND in that projection. Accepted because the alternative - loading every
item row to compute totals in memory for a list view - defeats the projection.

### Per-method `IDbContextFactory` context lifetime
Every service method creates and disposes its own context via
`await using var context = _contextFactory.CreateDbContext()`. No context outlives a
method. This kills the change-tracker collisions and memory bloat of the old
session-long-context pattern, and makes concurrent service calls safe (enables
`Task.WhenAll` in loops).

### Restrict vs. cascade on delete, per record's independent value
`ATRequest -> Person` is `Restrict`: a payment request carries snapshot columns and
survives the client's deletion (it's a financial record of its own). `ATRequestItem ->
ATRequest` is `Cascade`: line items are worthless orphaned from their request. `Note`
and `Form` cascade from `Person` for the same reason items do - no independent value.
The delete behavior encodes whether the child is a record in its own right.

---


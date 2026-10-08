<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Provider directory

### Passthrough is orthogonal to waiver services
`ProvidesPassthroughService` is a standalone bool, NOT a member of the `WaiverService`
flags enum, even though its checkbox renders among the waiver-service checkboxes. Maine
AT Solutions proves the axes are independent: it offers AT Assessments (a waiver service)
AND provides passthrough - two unrelated facts. Keeping passthrough separate makes "who
can the AT page pick" a clean `where ProvidesPassthroughService` with no enum-member
exception.

### Maine AT Solutions is a seed row, not a hardcoded special case
The statewide passthrough default is a `Provider` row with `ProvidesPassthroughService =
true`, seeded in the migration, pointed at by `Settings.DefaultPassthroughProviderId`.
Nothing branches on its identity. Change the setting and the default moves; the dropdown
is just "every passthrough provider."

**Rejected:** a magic "Maine AT Solutions" string or dropdown option. It would spread an
identity check across the AT page and settings.

### `DefaultPassthroughProviderId`: nullable FK, no SQL default
The FK deliberately has NO `HasDefaultValue`. A SQL default of 1 would backfill the
existing Settings row at `AddColumn` time - which can fire before the Provider seed
inserts row 1, an FK violation. Nullable-null is safe; the default gets set through the
Settings window instead. `OnDelete(SetNull)` so deleting the current default provider
clears the setting rather than blocking the delete (or stranding a retired agency).

### `[Flags]` bitmask for `OfferedServices`, not a join table
Four fixed, statutory waiver services stored as an int bitmask - the idiomatic .NET
choice. This IS a denormalization, named openly because Sati otherwise hearts normalized
structures: a `ProviderOfferedService` join table is the "correct" 3NF shape. Rejected as
ceremony for no payoff while the offering data is inert (nothing consumes it until
client<->provider links exist). Revisit if the service list becomes dynamic or needs
per-offering metadata.

### Structured provider address
Street/City/State/Zip as separate columns, mirroring `Agency` - not a single address
string. Reference data earns normalization even before anything parses it.

---


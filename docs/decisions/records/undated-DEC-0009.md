<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## ViewModels & UI

### Write-through observable wrappers over plain entity POCOs
Entities (`ATRequest`, `Provider`, `ATRequestItem`) stay INotifyPropertyChanged-free.
Each gets an editor VM (`ATRequestEditorViewModel`, `ProviderEditorViewModel`, etc.)
whose bindable properties read and write the entity directly. The entity is always
current, so Save just persists it - no copy-back step. The VM is a notifying lens, not
a second store.

**Rejected:** INPC on the entities. It couples the domain model to WPF and muddies which
object is the source of truth.

### Item-total notification via injected callback, not per-row subscription
`ATRequestItemEditorViewModel` takes an `Action` from its parent and invokes it only
when cost/quantity change (Name/URL don't move money). Cheaper and clearer than the
parent subscribing to each row's `PropertyChanged` and filtering by property name - the
row already knows which of its own edits affect totals.

### One bool per `[Flags]` bit in the provider editor
The four waiver-service checkboxes each map to one bit of `Provider.OfferedServices`:
set with `| flag`, clear with `& ~flag`. The editor VM does the bit math so the XAML
binds plain bools.

### Master-detail over inline grid for provider CRUD
Providers edit as list-plus-form, not an editable DataGrid. The passthrough checkbox
reveals three conditional billing fields - a "check this, three fields appear"
interaction a grid cell can't do gracefully. Consistent with the AT page and client
patterns.

### Deferred Save + PDF as one batch
The AT request editor has no Save yet, by design. `NewRequest` builds in memory,
`CloseEditor` discards. Save is bundled with the future Publish-PDF feature because
both are the same trip to disk - designing persistence once with both callers in view
beats bolting Save on now and refactoring when export lands.

---


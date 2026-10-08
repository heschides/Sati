<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Scope calls (things deliberately NOT built)

### URL stored, not scraped
`ATRequestItem.Url` is a plain stored field. Auto-extracting name/price/cost from a
retailer URL was scoped and rejected: retailers defeat scraping, rearrange their DOM,
and wall bots - and an outbound HTTP scraper is out of place in an app steering toward
HIPAA. The URL feeds a future screenshots-with-clickable-links page, entered by hand.

### URL not on the page-1 OADS form
The item table on `ATFormDocument` is a faithful reproduction of the state form, which
has no URL column. The URL is internal metadata surfaced in the app and (later) on
page 2, never on the filed document.

### Client<->provider association is a separate slice

The AT passthrough dropdown lists *all* passthrough providers and can't pre-select
"this client's home-support agency" because Sati has no consumer->provider link yet.
That association is its own model and slice; the four `OfferedServices` flags are inert
until it lands.


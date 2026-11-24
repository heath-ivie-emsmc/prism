# Metrics

The available Metrics in the Metric Catalog (`src/Prism.Application/Metrics/MetricCatalog.cs`). Every one is Provider scoped.
The Division column says whether it honors the Reporting Division filter (`division` dimension, `DivisionSupport.Supported`): Metrics that read `reporting.trip_detail_snapshot` do, and so do `net_collections` and `what_changed`, which
add up from the ledger joined to it when a division is chosen. The forecast and clean claim tables have no division, so those two do not. Each needs an
integration test (see `MetricCatalogTests`). The table below is generated from the catalog: do not edit it by hand.
`payer_mix_detail` is a drill target: reached from the `drill` link on a `payer_mix` share, not placeable on a Saved View (no presentations). Source tables are described in `warehouse.md`. See `adr/0007-metric-catalog-v2.md`.

<!-- generated:start -->
| Key | Category | Unit | Applies to | Service lines | Freshness | Division | Drills | Placeable | Renderer folder (`web/src/app/metrics/`) | Backend folder (`Metrics/` in Application, Infrastructure, Api) |
|---|---|---|---|---|---|---|---|---|---|---|
| `net_collections` | Cash | USD | both | air, ground, both | DailyLoad | Supported | none | yes | `net-collections/` | `NetCollections` |
| `net_cash_projection` | Cash | USD | both | air, ground, both | DailyLoad | None | none | yes | `net-cash-projection/` | `NetCashProjection` |
| `net_cash_breakdown` | Cash | USD | both | air, ground, both | DailyLoad | Supported | drill to `payer_mix` (binds `group`) | yes | `net-cash-breakdown/` | `NetCashBreakdown` |
| `net_cash_by_group` | Cash | USD | both | air, ground, both | DailyLoad | Supported | none | yes | `net-cash-by-group/` | `NetCashByGroup` |
| `cash_per_transport_by_payer` | Cash | USD per transport | both | air, ground, both | DailyLoad | Supported | none | yes | `cash-per-transport-by-payer/` | `CashPerTransport` |
| `payment_age` | Payers | USD | both | air, ground, both | DailyLoad | Supported | none | yes | `payment-age/` | `PaymentAge` |
| `payer_mix` | Payers | transports | both | air, ground, both | DailyLoad | Supported | drill to `payer_mix_detail` (binds `payerGroup`) | yes | `payer-mix/` | `PayerMix` |
| `transport_volume` | Transports | transports | both | air, ground, both | DailyLoad | Supported | none | yes | `transport-volume/` | `TransportVolume` |
| `clean_claim_rate` | Claims and AR | percent | both | air, ground, both | DailyLoad | None | none | yes | `clean-claim-rate/` | `CleanClaimRate` |
| `transport_mix_by_call_type` | Transports | transports | both | air, ground, both | DailyLoad | Supported | none | yes | `transport-mix-by-call-type/` | `TransportMixByCallType` |
| `aircraft_performance` | Fleet and bases | transports | air | air, both | DailyLoad | Supported | none | yes | `aircraft-performance/` | `AircraftPerformance` |
| `base_analysis` | Fleet and bases | transports | both | air, ground, both | DailyLoad | Supported | none | yes | `base-analysis/` | `BaseAnalysis` |
| `loaded_miles_by_aircraft` | Fleet and bases | miles | air | air, both | DailyLoad | Supported | none | yes | `loaded-miles-by-aircraft/` | `LoadedMilesByAircraft` |
| `payer_mix_financial_effect` | Payers | USD | both | air, ground, both | DailyLoad | Supported | none | yes | `payer-mix-financial-effect/` | `PayerMixFinancialEffect` |
| `cash_contribution_by_aircraft` | Cash | USD | air | air, both | DailyLoad | Supported | none | yes | `cash-contribution-by-aircraft/` | `CashContributionByAircraft` |
| `cash_contribution_by_division` | Cash | USD | both | air, ground, both | DailyLoad | Supported | none | yes | `cash-contribution-by-division/` | `CashContributionByDivision` |
| `cash_contribution_by_payer` | Cash | USD | both | air, ground, both | DailyLoad | Supported | drill to `payer_mix_detail` (binds `payerGroup`) | yes | `cash-contribution-by-payer/` | `CashContributionByPayer` |
| `transports_by_pickup_location` | Transports | transports | both | air, ground, both | DailyLoad | Supported | none | yes | `transports-by-pickup-location/` | `TransportsByPickupLocation` |
| `payer_mix_detail` | Payers | transports | both | air, ground, both | DailyLoad | Supported | none | no | `payer-mix-detail/` | `PayerMixDetail` |
| `what_changed` | Insights | text | both | air, ground, both | DailyLoad | Supported | none | no | none | `Insights` |
<!-- generated:end -->

The generated table and `web/src/app/metrics/catalog.fixture.json` come from the catalog. Regenerate both with `PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests --filter MetricCatalogSnapshotTests`. The registry tests catch drift on the web side.

## Adding a Metric

1. Add the slice folders in Application, Infrastructure and Api, with an `IMetricModule` (set its `RouteOrder`).
2. Add the renderer folder under `web/src/app/metrics/<kebab-key>/` and register it in `METRIC_RENDERERS`.
3. Add the help text in the SPA help registry under the Metric's `helpKey`.
4. Add the `MetricCatalog` entry, then regenerate this table and the catalog fixture by running the snapshot test with `PRISM_UPDATE_SNAPSHOTS=1`. Never edit either by hand.

The snapshot test, the registry tests and the help registry spec fail the build if any step is missing.

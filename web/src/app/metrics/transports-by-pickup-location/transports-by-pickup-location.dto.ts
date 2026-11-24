import { MetricEnvelopeDto, PayerShareDto } from '../shared/figures.dto';

/** One kind of pickup: scene, residence, custodial facility, hospital, skilled nursing, physician office, transfer site, diagnostic site, dialysis or other. */
export interface PickupSourceDto {
  readonly key: string;
  readonly name: string;
  /** True for the kinds of pickup that are a named facility. Scene, residence, custodial and other never have names. */
  readonly facilityBased: boolean;
  readonly transports: number;
  readonly priorTransports: number;
  readonly changeTransports: number;
  readonly share: number | null;
  readonly priorShare: number | null;
  readonly changePoints: number | null;
  readonly loadedMiles: number;
  readonly priorLoadedMiles: number;
  /** Cash posted in the month: a separate figure from the counts, which are by date of service. */
  readonly netCash: number;
  readonly priorNetCash: number;
  readonly payerMix: readonly PayerShareDto[];
}

/** One named facility, or the Other row. The name is warehouse text and can hold markup: render it only through Angular bindings. */
export interface PickupFacilityDto {
  readonly sourceKey: string | null;
  readonly name: string;
  readonly isOther: boolean;
  readonly transports: number;
  readonly priorTransports: number;
  readonly changeTransports: number;
  readonly share: number | null;
  readonly loadedMiles: number;
}

/** Response of GET /api/v1/providers/{providerId}/metrics/net-collections/transports-by-pickup-location. */
export interface TransportsByPickupLocationDto extends MetricEnvelopeDto {
  readonly totalTransports: number;
  readonly priorTotalTransports: number;
  readonly sources: readonly PickupSourceDto[];
  readonly facilities: readonly PickupFacilityDto[];
}

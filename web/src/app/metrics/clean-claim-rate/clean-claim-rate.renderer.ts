import { MetricRenderer } from '../metric-renderer';
import { loadCleanClaims } from './clean-claim-rate.loader';
import { fromCleanClaims } from './clean-claim-rate.widget';
import { CleanClaimRateDto } from './clean-claim-rate.dto';

export const CLEAN_CLAIM_RATE_RENDERER: MetricRenderer<CleanClaimRateDto> = {
  loader: loadCleanClaims,
  toWidgetData: fromCleanClaims,
  presentations: ['tile', 'line', 'table'],
};

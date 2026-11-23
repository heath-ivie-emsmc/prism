import { MetricRenderer } from '../metric-renderer';
import { loadPayerMix } from './payer-mix.loader';
import { fromPayerMix } from './payer-mix.widget';
import { PayerMixDto } from './payer-mix.dto';

export const PAYER_MIX_RENDERER: MetricRenderer<PayerMixDto> = {
  loader: loadPayerMix,
  toWidgetData: fromPayerMix,
  presentations: ['column', 'stacked', 'donut', 'table'],
};

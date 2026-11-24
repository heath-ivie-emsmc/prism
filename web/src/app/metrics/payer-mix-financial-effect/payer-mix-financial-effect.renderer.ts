import { MetricRenderer } from '../metric-renderer';
import { loadPayerMixFinancialEffect } from './payer-mix-financial-effect.loader';
import { fromPayerMixFinancialEffect } from './payer-mix-financial-effect.widget';
import { PayerMixFinancialEffectDto } from './payer-mix-financial-effect.dto';

export const PAYER_MIX_FINANCIAL_EFFECT_RENDERER: MetricRenderer<PayerMixFinancialEffectDto> = {
  loader: loadPayerMixFinancialEffect,
  toWidgetData: fromPayerMixFinancialEffect,
  presentations: ['column', 'tile', 'table'],
};

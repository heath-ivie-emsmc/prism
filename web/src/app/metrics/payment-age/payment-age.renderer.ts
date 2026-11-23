import { MetricRenderer } from '../metric-renderer';
import { loadPaymentAge } from './payment-age.loader';
import { fromPaymentAge } from './payment-age.widget';
import { PaymentAgeDto } from './payment-age.dto';

export const PAYMENT_AGE_RENDERER: MetricRenderer<PaymentAgeDto> = {
  loader: loadPaymentAge,
  toWidgetData: fromPaymentAge,
  presentations: ['table'],
};

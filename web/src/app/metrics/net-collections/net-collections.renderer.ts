import { MetricRenderer } from '../metric-renderer';
import { loadNetCollections } from './net-collections.loader';
import { fromNetCash } from './net-collections.widget';
import { NetCollectionsDto } from './net-collections.dto';

export const NET_COLLECTIONS_RENDERER: MetricRenderer<NetCollectionsDto> = {
  loader: loadNetCollections,
  toWidgetData: fromNetCash,
  presentations: ['line', 'column', 'table'],
};

import { MetricRenderer } from '../metric-renderer';
import { loadTransportVolume } from './transport-volume.loader';
import { fromTransports } from './transport-volume.widget';
import { TransportVolumeDto } from './transport-volume.dto';

export const TRANSPORT_VOLUME_RENDERER: MetricRenderer<TransportVolumeDto> = {
  loader: loadTransportVolume,
  toWidgetData: fromTransports,
  presentations: ['tile', 'column', 'table'],
};

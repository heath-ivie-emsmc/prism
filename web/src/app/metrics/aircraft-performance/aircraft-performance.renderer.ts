import { MetricRenderer } from '../metric-renderer';
import { loadAircraftPerformance } from './aircraft-performance.loader';
import { fromAircraftPerformance } from './aircraft-performance.widget';
import { AircraftPerformanceDto } from './aircraft-performance.dto';

export const AIRCRAFT_PERFORMANCE_RENDERER: MetricRenderer<AircraftPerformanceDto> = {
  loader: loadAircraftPerformance,
  toWidgetData: fromAircraftPerformance,
  presentations: ['table', 'column'],
};

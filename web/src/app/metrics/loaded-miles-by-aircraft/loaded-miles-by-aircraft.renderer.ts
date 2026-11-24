import { MetricRenderer } from '../metric-renderer';
import { loadLoadedMilesByAircraft } from './loaded-miles-by-aircraft.loader';
import { fromLoadedMilesByAircraft } from './loaded-miles-by-aircraft.widget';
import { LoadedMilesByAircraftDto } from './loaded-miles-by-aircraft.dto';

export const LOADED_MILES_BY_AIRCRAFT_RENDERER: MetricRenderer<LoadedMilesByAircraftDto> = {
  loader: loadLoadedMilesByAircraft,
  toWidgetData: fromLoadedMilesByAircraft,
  presentations: ['column', 'line', 'table'],
};

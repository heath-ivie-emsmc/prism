import { MetricRenderer } from '../metric-renderer';
import { loadBaseAnalysis } from './base-analysis.loader';
import { fromBaseAnalysis } from './base-analysis.widget';
import { BaseAnalysisDto } from './base-analysis.dto';

export const BASE_ANALYSIS_RENDERER: MetricRenderer<BaseAnalysisDto> = {
  loader: loadBaseAnalysis,
  toWidgetData: fromBaseAnalysis,
  presentations: ['table', 'column'],
};

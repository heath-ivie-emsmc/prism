import { BarChart, CustomChart, LineChart, PieChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer, SVGRenderer } from 'echarts/renderers';

/**
 * Only the pieces Prism draws are registered, so the rest of ECharts stays out of the bundle.
 * This module is imported lazily by the chart component, which keeps ECharts out of the initial download.
 * Add a chart type here when a Metric needs it, and check the lazy chunk size.
 */
echarts.use([LineChart, BarChart, PieChart, CustomChart, AriaComponent, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer, SVGRenderer]);

export { echarts };

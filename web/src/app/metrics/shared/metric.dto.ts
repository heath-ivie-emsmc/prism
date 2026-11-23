/** What a Metric is, as the API describes it. */
export interface MetricDto {
  readonly key: string;
  readonly name: string;
  readonly description: string;
  readonly unit: string;
}

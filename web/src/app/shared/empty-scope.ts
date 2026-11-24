import { Labels } from '../dashboard/service-line';

/**
 * What narrowed an answer: the Service Line when it is one line of several the Provider has, and the Reporting Division when the Metric applied one.
 * A Metric that does not use a filter must not pass it, so an empty answer is never blamed on a filter that had no effect.
 */
export interface EmptyScope {
  /** Air or ground when the page is on one line the Provider could have widened. Null or `all` when the line narrows nothing. */
  readonly line: 'air' | 'ground' | 'all' | null;
  /** True when the Metric applied the chosen Reporting Division. */
  readonly divisionApplied: boolean;
  /** The chosen division's name (warehouse text, shown as text), or undefined when it is not known yet: the Label is used then. */
  readonly divisionName?: string | undefined;
  readonly labels: Labels;
}

const LINE_TEXT = { air: 'Air only', ground: 'Ground only' } as const;
const WIDEST_LINE = 'Air + ground';

/** The filters that narrowed the answer, in words: "Mercy Flights - Rotor under Air only". Empty when nothing narrowed it. */
function narrowedBy(scope: EmptyScope): { division: string; line: string } {
  const division = scope.divisionApplied ? (scope.divisionName || `the chosen ${scope.labels.reportingDivision.toLowerCase()}`) : '';
  const line = scope.line === 'air' || scope.line === 'ground' ? LINE_TEXT[scope.line] : '';
  return { division, line };
}

/** True when a filter the user chose narrowed the answer, so an empty one may be because of it. */
export function isNarrowed(scope: EmptyScope): boolean {
  const { division, line } = narrowedBy(scope);
  return division !== '' || line !== '';
}

/**
 * The sentence for an empty answer. With no narrowing filter it is `plain` as it always was. Otherwise it names the scope and what to try:
 * "No net cash collected for Mercy Flights - Ground Critical Care under Air only in September 2026. Try All bases or Air + ground."
 * `subject` is lower case ("net cash collected"). One place, so a widget, a Metric page and a dashboard panel say the same thing.
 */
export function emptyMessage(subject: string, period: string, plain: string, scope: EmptyScope): string {
  const { division, line } = narrowedBy(scope);
  if (!division && !line) return plain;
  const where = [division ? `for ${division}` : '', line ? `under ${line}` : ''].filter(Boolean).join(' ');
  const tries = [division ? scope.labels.allReportingDivisions : '', line ? WIDEST_LINE : ''].filter(Boolean).join(' or ');
  return `No ${subject} ${where} in ${period}. Try ${tries}.`;
}

/** Plain-language help for one Metric: a one-line summary and the terms a reader may not know. */
export interface MetricHelp {
  readonly summary: string;
  readonly glossary: readonly { readonly term: string; readonly text: string }[];
}

/** The renamable terms help text may name, as the Labels spell them. */
export interface HelpLabels {
  readonly reportingDivision: string;
}

/**
 * Fills the Label placeholders in help text. `{reportingDivision}` becomes the Provider's Label in lower case for use inside a sentence,
 * and `{ReportingDivision}` keeps the Label's own capitals. Help text never hard-codes a renamable term.
 */
export function resolveHelpText(text: string, labels: HelpLabels): string {
  return text.replaceAll('{reportingDivision}', labels.reportingDivision.toLowerCase()).replaceAll('{ReportingDivision}', labels.reportingDivision);
}

export function resolveHelp(help: MetricHelp, labels: HelpLabels): MetricHelp {
  return {
    summary: resolveHelpText(help.summary, labels),
    glossary: help.glossary.map((g) => ({ term: resolveHelpText(g.term, labels), text: resolveHelpText(g.text, labels) })),
  };
}

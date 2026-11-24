import { DrillRoute, drillRoute } from '../drill-link';
import { NetCashBreakdownDto } from './net-cash-breakdown.dto';

export type BreakdownLine = 'air' | 'ground';

export interface BreakdownGroup {
  readonly key: string;
  readonly name: string;
  readonly icon: string;
  /** Net cash in the chosen month and the month before. */
  readonly cash: number;
  readonly priorCash: number;
  readonly transports: number;
  readonly priorTransports: number;
  /** Two shares that add up to 100, written "78% / 22%". */
  readonly mix: string;
  readonly loadedMiles: number;
  /** Where the group's card leads, read from the API's drill link, or null when the API gave none (the card then has no link). */
  readonly drill: DrillRoute | null;
}

export interface BreakdownRow {
  readonly key: string;
  readonly id: string;
  readonly groupName: string;
  readonly icon: string;
  readonly location: string;
  readonly transports: number;
  readonly transportsChange: number;
  readonly loadedMiles: number;
  readonly cash: number;
  readonly priorCash: number;
}

/** The words that change between Service Lines. */
export interface BreakdownTerms {
  readonly title: string;
  readonly groupHeading: string;
  readonly idHeading: string;
  readonly mixHeading: string;
}

export interface NetCashBreakdown {
  readonly line: BreakdownLine;
  readonly terms: BreakdownTerms;
  /** The accounting period is still open, so cash is still posting and the figures are not final. */
  readonly stillPosting: boolean;
  readonly groups: readonly BreakdownGroup[];
  readonly rows: readonly BreakdownRow[];
}

const TERMS: Record<BreakdownLine, BreakdownTerms> = {
  air: {
    title: 'Air net cash by aircraft type',
    groupHeading: 'Aircraft type',
    idHeading: 'Tail number',
    mixHeading: 'Scene / interfacility',
  },
  ground: {
    title: 'Ground net cash by level of service',
    groupHeading: 'Level of service',
    idHeading: 'Tail number',
    mixHeading: 'Emergency / scheduled',
  },
};

/** The title over the rows table, built from the Provider's Label for a Reporting Division (never typed here): "By base location and aircraft". */
export function breakdownTableTitle(line: BreakdownLine, reportingDivisionLabel: string): string {
  return `By ${reportingDivisionLabel.toLowerCase()} and ${line === 'air' ? 'aircraft' : 'vehicle'}`;
}

const ICONS: Record<string, string> = {
  'rotor-wing': 'helicopter',
  'fixed-wing': 'flight',
  als: 'local_hospital',
  als2: 'local_hospital',
  bls: 'medical_services',
  sct: 'monitor_heart',
};
const DEFAULT_ICON = 'medical_services';

/** "78% / 22%" from two counts, or a dash when there are none. The second share is what is left, so the two always add up to 100. */
export function mixText(primary: number, secondary: number): string {
  const total = primary + secondary;
  if (total === 0) return '—';
  const first = Math.round((primary / total) * 100);
  return `${first}% / ${100 - first}%`;
}

/** An empty page for a Service Line: what shows before the numbers arrive, or when there are none. */
export function emptyBreakdown(line: BreakdownLine): NetCashBreakdown {
  return { line, terms: TERMS[line], stillPosting: false, groups: [], rows: [] };
}

/** The API's answer as the page uses it: display words for the Service Line, icons, and the mix as text. */
export function toBreakdown(dto: NetCashBreakdownDto, line: BreakdownLine): NetCashBreakdown {
  const icon = (key: string): string => ICONS[key] ?? DEFAULT_ICON;
  return {
    line,
    terms: TERMS[line],
    stillPosting: dto.periodOpen,
    groups: dto.groups.map(
      (g): BreakdownGroup => ({
        key: g.key,
        name: g.name,
        icon: icon(g.key),
        cash: g.current.cash,
        priorCash: g.prior.cash,
        transports: g.current.transports,
        priorTransports: g.prior.transports,
        mix: mixText(g.current.primaryMix, g.current.secondaryMix),
        loadedMiles: Math.round(g.current.loadedMiles),
        drill: drillRoute(g.links),
      }),
    ),
    rows: dto.rows.map(
      (r, i): BreakdownRow => ({
        key: `${r.groupKey}|${r.base ?? ''}|${r.vehicle}|${i}`,
        id: r.vehicle,
        groupName: r.groupName,
        icon: icon(r.groupKey),
        location: r.base ?? '—',
        transports: r.current.transports,
        transportsChange: r.current.transports - r.prior.transports,
        loadedMiles: Math.round(r.current.loadedMiles),
        cash: r.current.cash,
        priorCash: r.prior.cash,
      }),
    ),
  };
}

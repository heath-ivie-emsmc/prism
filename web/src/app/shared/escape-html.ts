const ENTITIES: Readonly<Record<string, string>> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

/** Escapes text for ECharts' HTML tooltips, which render a formatter's string as markup. Every server-provided string (payer, group, division, period names) goes through this. */
export function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => ENTITIES[c]!);
}

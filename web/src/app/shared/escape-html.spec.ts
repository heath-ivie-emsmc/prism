import { describe, expect, it } from 'vitest';
import { escapeHtml } from './escape-html';

describe('escapeHtml', () => {
  it('escapes every character that can open or close markup', () => {
    expect(escapeHtml(`<a href="x" onclick='y'>&</a>`)).toBe('&lt;a href=&quot;x&quot; onclick=&#39;y&#39;&gt;&amp;&lt;/a&gt;');
  });

  it('leaves plain text alone', () => {
    expect(escapeHtml('Fixed wing 12%')).toBe('Fixed wing 12%');
  });
});

/** Pomocné parsery pro nepořádná data (různé formáty datumů, cen, diakritiky). */

export const stripDiacritics = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '');
export const normKey = (s: string) => stripDiacritics(s).toLowerCase().replace(/\s+/g, ' ').trim();

const pad = (n: number | string) => String(n).padStart(2, '0');

function validIso(y: number, m: number, d: number): string | null {
  const dt = new Date(Date.UTC(y, m - 1, d));
  if (dt.getUTCFullYear() !== y || dt.getUTCMonth() !== m - 1 || dt.getUTCDate() !== d) return null;
  return `${y}-${pad(m)}-${pad(d)}`;
}

/** Podporuje: 2019-01-06, 6. 1. 2019, 06.01.2019 a excelová sériová čísla (43552). null = nerozpoznáno/neplatné (např. 31. 9.). */
export function parseDate(raw: string | undefined | null): string | null {
  const s = (raw ?? '').trim();
  if (!s) return null;
  let m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
  if (m) return validIso(+m[1], +m[2], +m[3]);
  m = s.match(/^(\d{1,2})\.\s*(\d{1,2})\.\s*(\d{4})$/);
  if (m) return validIso(+m[3], +m[2], +m[1]);
  if (/^\d{5}$/.test(s)) {
    const serial = +s; // Excel epoch (1900 date system, s korekcí chyby 1900)
    const dt = new Date(Date.UTC(1899, 11, 30) + serial * 86400000);
    return dt.toISOString().slice(0, 10);
  }
  return null;
}

/** "1 500,00", "2 100 Kč", "380,- Kč", "zdarma" -> číslo; prázdné/nerozpoznané -> null. */
export function parseMoney(raw: string | undefined | null): number | null {
  const s = (raw ?? '').trim().toLowerCase();
  if (!s) return null;
  if (s === 'zdarma') return 0;
  const cleaned = s.replace(/kč/g, '').replace(/[\s ]/g, '').replace(',-', '').replace(',', '.');
  if (!/^-?\d+(\.\d+)?$/.test(cleaned)) return null;
  return Number(cleaned);
}

/** Přičte měsíce a zafixuje konec měsíce (31. 3. + 12 m => 31. 3.; 29. 2. + 12 m => 28. 2.). */
export function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const total = m - 1 + months;
  const ny = y + Math.floor(total / 12);
  const nm = ((total % 12) + 12) % 12;
  const last = new Date(Date.UTC(ny, nm + 1, 0)).getUTCDate();
  return `${ny}-${pad(nm + 1)}-${pad(Math.min(d, last))}`;
}

export function daysBetween(fromIso: string, toIso: string): number {
  return Math.round((Date.parse(toIso) - Date.parse(fromIso)) / 86400000);
}

export function isWeekend(iso: string): boolean {
  const d = new Date(iso + 'T00:00:00Z').getUTCDay();
  return d === 0 || d === 6;
}

export function titleCase(s: string): string {
  return s
    .trim()
    .toLowerCase()
    .replace(/(^|[\s-])(\p{L})/gu, (_, sep, ch) => sep + ch.toUpperCase());
}

/** Levenshtein pro tolerantní párování kódů (KYB3R -> KYBER). */
export function editDistance(a: string, b: string): number {
  const dp = Array.from({ length: a.length + 1 }, (_, i) => [i, ...Array(b.length).fill(0)]);
  for (let j = 1; j <= b.length; j++) dp[0][j] = j;
  for (let i = 1; i <= a.length; i++)
    for (let j = 1; j <= b.length; j++)
      dp[i][j] = Math.min(dp[i - 1][j] + 1, dp[i][j - 1] + 1, dp[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
  return dp[a.length][b.length];
}

/** Ochrana proti CSV/Excel injection při exportu (buňky začínající = + - @ ). */
export function csvSafe(v: string | number | null | undefined): string {
  let s = v == null ? '' : String(v);
  if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;
  return /[";\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

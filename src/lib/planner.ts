import type { Dataset, Term } from './types';
import { groupOf, type Requirement } from './compliance';

export interface Assignment {
  req: Requirement;
  term: Term;
  /** termín je až po vypršení platnosti / po lhůtě */
  pozde: boolean;
}

export interface Unassigned {
  req: Requirement;
  duvod: string;
}

export interface Plan {
  assignments: Assignment[];
  unassigned: Unassigned[];
  /** id termínu -> přiřazení */
  byTerm: Map<string, Assignment[]>;
  /** kód školení -> {potřeba, volných míst celkem, nepokryto} */
  poKurzu: Map<string, { potreba: number; volno: number; nepokryto: number }>;
  naklady: number;
}

export const freeSeats = (t: Term) => Math.max(0, t.kapacita - t.obsazeno);

const toMin = (s: string) => {
  const [h, m] = s.split(':').map(Number);
  return h * 60 + m;
};
function overlap(a: Term, b: Term): boolean {
  if (a.datum !== b.datum) return false;
  const [a1, a2] = a.cas.split('-').map(toMin);
  const [b1, b2] = b.cas.split('-').map(toMin);
  if ([a1, a2, b1, b2].some(Number.isNaN)) return true;
  return a1 < b2 && b1 < a2;
}

/**
 * Greedy plán „nejdřív nejnaléhavější“ (earliest-deadline-first):
 *  1. potřeby seřadí podle lhůty (prošlé/chybějící první, pak ty, kterým platnost končí dřív),
 *  2. každému přidělí nejbližší termín s volným místem, který stihne lhůtu,
 *  3. když žádný termín lhůtu nestihne, přidělí nejbližší volný a označí ho jako „pozdě“,
 *  4. když není žádné volné místo, zůstane nepřiřazený (signál: vypsat další termín).
 * Nepřihlašuje na termíny po datu ukončení pracovního poměru ani na kolidující termíny téhož člověka.
 */
export function buildPlan(ds: Dataset, reqs: Requirement[], ref: string): Plan {
  const terms = ds.terms.filter((t) => t.vyrazen === null && t.datum >= ref);
  const seats = new Map(terms.map((t) => [t.id, freeSeats(t)]));
  const demand = reqs
    .filter((r) => r.status !== 'platné' && groupOf(r.emp, ref) !== 'rodičovská' && groupOf(r.emp, ref) !== 'ukončen')
    .sort((a, b) => (a.deadline ?? '9999').localeCompare(b.deadline ?? '9999') || a.emp.id.localeCompare(b.emp.id) || a.course.kod.localeCompare(b.course.kod));

  const assignments: Assignment[] = [];
  const unassigned: Unassigned[] = [];
  const mine = new Map<string, Term[]>();
  for (const r of demand) {
    const candidates = terms.filter(
      (t) =>
        t.kod === r.course.kod &&
        (seats.get(t.id) ?? 0) > 0 &&
        (r.emp.ukonceni === null || t.datum <= r.emp.ukonceni) &&
        !(mine.get(r.emp.id) ?? []).some((o) => overlap(o, t)),
    );
    if (!candidates.length) {
      const hasTerm = terms.some((t) => t.kod === r.course.kod);
      unassigned.push({ req: r, duvod: hasTerm ? 'Všechny termíny jsou plné (nebo kolidují / jsou po ukončení poměru)' : 'Není vypsán žádný použitelný termín' });
      continue;
    }
    const inTime = candidates.find((t) => r.deadline === null || t.datum <= r.deadline);
    const term = inTime ?? candidates[0];
    seats.set(term.id, seats.get(term.id)! - 1);
    mine.set(r.emp.id, [...(mine.get(r.emp.id) ?? []), term]);
    assignments.push({ req: r, term, pozde: !inTime });
  }

  const byTerm = new Map<string, Assignment[]>();
  for (const a of assignments) byTerm.set(a.term.id, [...(byTerm.get(a.term.id) ?? []), a]);
  const poKurzu = new Map<string, { potreba: number; volno: number; nepokryto: number }>();
  for (const c of ds.courses) {
    const potreba = demand.filter((r) => r.course.kod === c.kod).length;
    if (!potreba) continue;
    const volno = terms.filter((t) => t.kod === c.kod).reduce((s, t) => s + freeSeats(t), 0);
    poKurzu.set(c.kod, { potreba, volno, nepokryto: unassigned.filter((u) => u.req.course.kod === c.kod).length });
  }
  const naklady = assignments.reduce((s, a) => s + (a.term.cena ?? 0), 0);
  return { assignments, unassigned, byTerm, poKurzu, naklady };
}

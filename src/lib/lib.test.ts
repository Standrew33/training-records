import { describe, expect, it } from 'vitest';
import { addMonths, csvSafe, parseDate, parseMoney, titleCase } from './parse';
import { computeRequirements } from './compliance';
import { buildPlan } from './planner';
import type { Dataset, Employee, Term } from './types';

describe('parse', () => {
  it('parses all date formats and rejects impossible dates', () => {
    expect(parseDate('2019-01-06')).toBe('2019-01-06');
    expect(parseDate('6. 1. 2019')).toBe('2019-01-06');
    expect(parseDate('06.01.2019')).toBe('2019-01-06');
    expect(parseDate('43552')).toBe('2019-03-28');
    expect(parseDate('31. 9. 2026')).toBeNull();
    expect(parseDate('')).toBeNull();
  });
  it('parses money', () => {
    expect(parseMoney('1 500,00')).toBe(1500);
    expect(parseMoney('2 100 Kč')).toBe(2100);
    expect(parseMoney('380,- Kč')).toBe(380);
    expect(parseMoney('zdarma')).toBe(0);
    expect(parseMoney('')).toBeNull();
  });
  it('adds months with end-of-month clamp', () => {
    expect(addMonths('2024-02-29', 12)).toBe('2025-02-28');
    expect(addMonths('2019-03-31', 24)).toBe('2021-03-31');
  });
  it('title-cases names and neutralizes CSV formulas', () => {
    expect(titleCase('  LUCIE')).toBe('Lucie');
    expect(titleCase('růžička')).toBe('Růžička');
    expect(csvSafe('=HYPERLINK("x")')).toBe(`"'=HYPERLINK(""x"")"`);
  });
});

const emp = (id: string, extra: Partial<Employee> = {}): Employee => ({
  id, jmeno: 'A', prijmeni: id, email: id + '@x.cz', utvar: 'IT oddělení', utvarOdvozen: false, pozice: 'Technik',
  vedouciEmail: '', vedouciId: null, nastup: '2015-01-01', ukonceni: null, stav: 'aktivní', ...extra,
});
const term = (id: string, datum: string, kapacita: number, obsazeno = 0): Term => ({
  id, kod: 'KYBER', datum, cas: '09:00-12:00', misto: 'A', kapacita, obsazeno, cena: 800, cenaOdhad: false, stav: 'plánováno', vyrazen: null, vikend: false,
});
const ds = (employees: Employee[], completions: Dataset['completions'], terms: Term[]): Dataset => ({
  exportDatum: '2026-10-01', employees, completions, terms, issues: [],
  courses: [{ kod: 'KYBER', nazev: 'Kyber', platnostMesicu: 12, povinnost: { typ: 'VSICHNI' } }],
});

describe('compliance + plan', () => {
  const c = (empId: string, datum: string) => ({ id: 'Z' + empId + datum, empId, kod: 'KYBER', datum, platnostDoZdroj: null, cena: null, poznamka: '' });
  it('classifies statuses', () => {
    const d = ds([emp('E1'), emp('E2'), emp('E3'), emp('E4')], [c('E1', '2026-05-01'), c('E2', '2025-09-01'), c('E3', '2025-12-01')], []);
    const s = Object.fromEntries(computeRequirements(d, '2026-10-01', 90).map((r) => [r.emp.id, r.status]));
    expect(s).toEqual({ E1: 'platné', E2: 'prošlé', E3: 'brzy', E4: 'chybí' });
  });
  it('skips terminated and parental leave in the plan, respects capacity and urgency', () => {
    const emps = [emp('E1'), emp('E2'), emp('E3'), emp('E4', { stav: 'rodičovská' }), emp('E5', { stav: 'ukončen' })];
    const d = ds(emps, [c('E3', '2025-12-01')], [term('T1', '2026-10-10', 2), term('T2', '2026-11-10', 5)]);
    const reqs = computeRequirements(d, '2026-10-01', 90);
    const plan = buildPlan(d, reqs, '2026-10-01');
    expect(plan.assignments.length).toBe(3);
    expect(plan.byTerm.get('T1')!.length).toBe(2); // kapacita 2 na dřívějším termínu
    const e3 = plan.assignments.find((a) => a.req.emp.id === 'E3')!;
    expect(e3.term.id).toBe('T2'); // E1/E2 (chybí) mají přednost o dřívější termín; E3 platnost končí až 1. 12. a T2 to stihne
    expect(e3.pozde).toBe(false);
  });
});

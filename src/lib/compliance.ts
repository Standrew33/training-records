import type { Course, Dataset, Employee } from './types';
import { addMonths, daysBetween, normKey } from './parse';

export type Status = 'platné' | 'brzy' | 'prošlé' | 'chybí';

/** Počet dní po nástupu, do kdy musí nový zaměstnanec povinná školení absolvovat (předpoklad). */
export const HIRE_GRACE_DAYS = 30;

export interface Requirement {
  emp: Employee;
  course: Course;
  status: Status;
  lastDate: string | null;
  validUntil: string | null; // null = bez expirace nebo nikdy absolvováno
  daysLeft: number | null; // záporné = po expiraci
  /** Do kdy je nutné školení mít, aby nevypršelo / nebylo po lhůtě. */
  deadline: string | null;
}

export type Group = 'aktivní' | 'nastupuje' | 'rodičovská' | 'ukončen';

export function groupOf(e: Employee, ref: string): Group {
  if (e.stav === 'ukončen' || (e.ukonceni !== null && e.ukonceni <= ref)) return 'ukončen';
  if (e.stav === 'rodičovská') return 'rodičovská';
  if (e.nastup !== null && e.nastup > ref) return 'nastupuje';
  return 'aktivní';
}

export function isRequired(course: Course, e: Employee): boolean {
  const p = course.povinnost;
  switch (p.typ) {
    case 'VSICHNI':
      return true;
    case 'UTVAR':
      return p.hodnoty.some((h) => normKey(h) === normKey(e.utvar));
    case 'POZICE':
      return p.hodnoty.some((h) => normKey(h) === normKey(e.pozice));
    default:
      return false;
  }
}

export function computeRequirements(ds: Dataset, ref: string, soonDays: number): Requirement[] {
  const latest = new Map<string, string>();
  for (const c of ds.completions) {
    if (c.datum > ref) continue;
    const k = c.empId + '|' + c.kod;
    if (!latest.has(k) || latest.get(k)! < c.datum) latest.set(k, c.datum);
  }
  const out: Requirement[] = [];
  for (const emp of ds.employees) {
    if (groupOf(emp, ref) === 'ukončen') continue;
    for (const course of ds.courses) {
      if (!isRequired(course, emp)) continue;
      const lastDate = latest.get(emp.id + '|' + course.kod) ?? null;
      let status: Status;
      let validUntil: string | null = null;
      let daysLeft: number | null = null;
      let deadline: string | null = null;
      if (!lastDate) {
        status = 'chybí';
        const base = emp.nastup ?? ref;
        const due = addDays(base, HIRE_GRACE_DAYS);
        deadline = emp.nastup && due > ref ? due : ref;
      } else if (!course.platnostMesicu) {
        status = 'platné'; // 0 / neuvedeno = bez expirace
      } else {
        validUntil = addMonths(lastDate, course.platnostMesicu);
        daysLeft = daysBetween(ref, validUntil);
        deadline = validUntil;
        status = daysLeft < 0 ? 'prošlé' : daysLeft <= soonDays ? 'brzy' : 'platné';
      }
      out.push({ emp, course, status, lastDate, validUntil, daysLeft, deadline });
    }
  }
  return out;
}

export function addDays(iso: string, n: number): string {
  return new Date(Date.parse(iso) + n * 86400000).toISOString().slice(0, 10);
}

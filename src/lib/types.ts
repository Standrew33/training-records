export type Severity = 'chyba' | 'varování' | 'info';

export interface Issue {
  zdroj: 'zaměstnanci' | 'absolvovaná' | 'katalog' | 'termíny';
  severity: Severity;
  ref: string; // id řádku / zaměstnance / termínu
  popis: string;
  rizeni: string; // co se s tím stalo
}

export interface Employee {
  id: string;
  jmeno: string;
  prijmeni: string;
  email: string;
  utvar: string;
  utvarOdvozen: boolean;
  pozice: string;
  vedouciEmail: string;
  vedouciId: string | null;
  nastup: string | null; // ISO
  ukonceni: string | null; // ISO
  stav: 'aktivní' | 'ukončen' | 'rodičovská';
}

export type Povinnost =
  | { typ: 'VSICHNI' }
  | { typ: 'UTVAR'; hodnoty: string[] }
  | { typ: 'POZICE'; hodnoty: string[] }
  | { typ: 'NEPOVINNE' };

export interface Course {
  kod: string;
  nazev: string;
  platnostMesicu: number | null; // null = neuvedeno (nepovinné), 0 = bez expirace
  povinnost: Povinnost;
}

export interface Completion {
  id: string;
  empId: string;
  kod: string;
  datum: string; // ISO
  platnostDoZdroj: string | null;
  cena: number | null;
  poznamka: string;
}

export interface Term {
  id: string;
  kod: string;
  datum: string; // ISO
  cas: string;
  misto: string;
  kapacita: number;
  obsazeno: number;
  cena: number | null;
  cenaOdhad: boolean;
  stav: string;
  /** null = použitelný, jinak důvod vyřazení z plánování */
  vyrazen: string | null;
  vikend: boolean;
}

export interface Dataset {
  exportDatum: string;
  employees: Employee[];
  courses: Course[];
  completions: Completion[];
  terms: Term[];
  issues: Issue[];
}

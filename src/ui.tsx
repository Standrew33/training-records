import type { ReactNode } from 'react';
import type { Dataset, Employee } from './lib/types';
import type { Requirement, Status } from './lib/compliance';
import { csvSafe } from './lib/parse';

export interface Ctx {
  ds: Dataset;
  ref: string;
  soon: number;
  reqs: Requirement[];
  empById: Map<string, Employee>;
  nav: (tab: Tab, filter?: Partial<PeopleFilter>) => void;
}

export type Tab = 'prehled' | 'lide' | 'plan' | 'terminy' | 'kvalita';

export interface PeopleFilter {
  q: string;
  utvar: string;
  vedouci: string;
  kurz: string;
  stav: 'problem' | 'vse' | Status;
}
export const emptyFilter: PeopleFilter = { q: '', utvar: '', vedouci: '', kurz: '', stav: 'problem' };

export const fmtDate = (iso: string | null | undefined) => {
  if (!iso) return '–';
  const [y, m, d] = iso.split('-');
  return `${+d}. ${+m}. ${y}`;
};
export const fmtKc = (n: number) => n.toLocaleString('cs-CZ') + ' Kč';
export const fullName = (e: Employee) => `${e.prijmeni} ${e.jmeno}`;

export const STATUS_LABEL: Record<Status, string> = { platné: 'Platné', brzy: 'Brzy vyprší', prošlé: 'Prošlé', chybí: 'Chybí' };
export const STATUS_CLASS: Record<Status, string> = { platné: 'ok', brzy: 'soon', prošlé: 'bad', chybí: 'miss' };

export function Badge({ status, children }: { status: Status; children?: ReactNode }) {
  return <span className={`badge ${STATUS_CLASS[status]}`}>{children ?? STATUS_LABEL[status]}</span>;
}

export function downloadCsv(name: string, header: string[], rows: (string | number | null)[][]) {
  const text = '﻿' + [header, ...rows].map((r) => r.map(csvSafe).join(';')).join('\r\n');
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

export function StackBar({ parts }: { parts: { cls: string; n: number; label: string }[] }) {
  const total = parts.reduce((s, p) => s + p.n, 0) || 1;
  return (
    <div className="stack" role="img" aria-label={parts.map((p) => `${p.label}: ${p.n}`).join(', ')}>
      {parts.map((p) => p.n > 0 && <div key={p.cls} className={`seg ${p.cls}`} style={{ width: `${(p.n / total) * 100}%` }} title={`${p.label}: ${p.n}`} />)}
    </div>
  );
}

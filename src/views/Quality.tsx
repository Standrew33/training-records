import { useState } from 'react';
import type { Severity } from '../lib/types';
import type { Ctx } from '../ui';

const SEV: Severity[] = ['chyba', 'varování', 'info'];

export default function Quality({ ctx }: { ctx: Ctx }) {
  const { issues } = ctx.ds;
  const [sev, setSev] = useState<Severity | ''>('');
  const [src, setSrc] = useState('');
  const sources = [...new Set(issues.map((i) => i.zdroj))];
  const shown = issues.filter((i) => (!sev || i.severity === sev) && (!src || i.zdroj === src));
  return (
    <section className="card">
      <h2>Kvalita vstupních dat</h2>
      <p className="muted">
        Co jsme ve čtyřech exportech našli a jak s tím aplikace naložila. Nic se tiše „nedoplňuje“ – každá úprava je tu vidět. Hodnotu „chyba“ má problém, který změnil
        výsledky (vyřazený záznam); „varování“ je úprava, kterou by měl někdo ověřit; „info“ je kosmetika.
      </p>
      <div className="kpis small">
        {SEV.map((s) => (
          <button key={s} className={`kpi click ${sev === s ? 'sel' : ''}`} onClick={() => setSev(sev === s ? '' : s)}>
            <div className="n">{issues.filter((i) => i.severity === s).length}</div>
            {s}
          </button>
        ))}
      </div>
      <div className="filters">
        <select value={src} onChange={(e) => setSrc(e.target.value)} aria-label="Zdroj">
          <option value="">Všechny zdroje</option>
          {sources.map((s) => (
            <option key={s}>{s}</option>
          ))}
        </select>
        <span className="muted">{shown.length} záznamů</span>
      </div>
      <div className="tablewrap">
        <table>
          <thead>
            <tr>
              <th>Zdroj</th>
              <th>Typ</th>
              <th>Odkaz</th>
              <th>Problém</th>
              <th>Řešení</th>
            </tr>
          </thead>
          <tbody>
            {shown.map((i, n) => (
              <tr key={n}>
                <td>{i.zdroj}</td>
                <td>
                  <span className={`tag sev-${i.severity}`}>{i.severity}</span>
                </td>
                <td>{i.ref}</td>
                <td>{i.popis}</td>
                <td>{i.rizeni}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

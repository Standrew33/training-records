import { Fragment, useMemo, useState } from 'react';
import { groupOf, type Requirement } from '../lib/compliance';
import type { Plan } from '../lib/planner';
import { normKey } from '../lib/parse';
import { Badge, downloadCsv, fmtDate, fullName, STATUS_LABEL, type Ctx, type PeopleFilter } from '../ui';

const GROUP_NOTE: Record<string, string> = {
  rodičovská: 'rodičovská – do statistik se nezapočítává',
  nastupuje: 'nastupuje',
  ukončen: 'ukončen',
};

export default function People({ ctx, plan, filter, setFilter }: { ctx: Ctx; plan: Plan; filter: PeopleFilter; setFilter: (f: PeopleFilter) => void }) {
  const { ds, reqs, ref, empById } = ctx;
  const [open, setOpen] = useState<string | null>(null);
  const set = (p: Partial<PeopleFilter>) => setFilter({ ...filter, ...p });

  const emps = ds.employees.filter((e) => groupOf(e, ref) !== 'ukončen');
  const utvary = [...new Set(emps.map((e) => e.utvar))].sort();
  const vedouci = [...new Set(emps.map((e) => e.vedouciId).filter((x): x is string => !!x))].map((id) => empById.get(id)!).sort((a, b) => a.prijmeni.localeCompare(b.prijmeni, 'cs'));

  const planned = useMemo(() => {
    const m = new Map<string, string>();
    plan.assignments.forEach((a) => m.set(a.req.emp.id + '|' + a.req.course.kod, `${fmtDate(a.term.datum)} (${a.term.id})`));
    return m;
  }, [plan]);

  const rows = useMemo(() => {
    const q = normKey(filter.q);
    const byEmp = new Map<string, Requirement[]>();
    reqs.forEach((r) => byEmp.set(r.emp.id, [...(byEmp.get(r.emp.id) ?? []), r]));
    return emps
      .filter((e) => (!filter.utvar || e.utvar === filter.utvar) && (!filter.vedouci || e.vedouciId === filter.vedouci || e.id === filter.vedouci))
      .filter((e) => !q || normKey(`${e.jmeno} ${e.prijmeni} ${e.id} ${e.pozice}`).includes(q))
      .map((e) => {
        const rs = (byEmp.get(e.id) ?? []).filter((r) => !filter.kurz || r.course.kod === filter.kurz);
        const hit = rs.filter((r) => (filter.stav === 'vse' ? true : filter.stav === 'problem' ? r.status !== 'platné' : r.status === filter.stav));
        return { e, rs, hit };
      })
      .filter((x) => (filter.stav === 'vse' ? true : x.hit.length > 0))
      .sort((a, b) => b.hit.length - a.hit.length || a.e.prijmeni.localeCompare(b.e.prijmeni, 'cs'));
  }, [emps, reqs, filter]);

  const exportCsv = () =>
    downloadCsv(
      'lide-skoleni.csv',
      ['ID', 'Příjmení', 'Jméno', 'Útvar', 'Pozice', 'Nadřízený', 'Školení', 'Stav', 'Poslední absolvování', 'Platné do', 'Navržený termín'],
      rows.flatMap(({ e, hit }) =>
        hit.map((r) => [e.id, e.prijmeni, e.jmeno, e.utvar, e.pozice, e.vedouciId ? fullName(empById.get(e.vedouciId)!) : '', r.course.nazev, STATUS_LABEL[r.status], fmtDate(r.lastDate), fmtDate(r.validUntil), planned.get(e.id + '|' + r.course.kod) ?? '']),
      ),
    );

  return (
    <section className="card">
      <h2>Lidé a jejich školení</h2>
      <div className="filters">
        <input type="search" placeholder="Hledat jméno, ID, pozici…" value={filter.q} onChange={(e) => set({ q: e.target.value })} />
        <select value={filter.utvar} onChange={(e) => set({ utvar: e.target.value })} aria-label="Útvar">
          <option value="">Všechny útvary</option>
          {utvary.map((u) => (
            <option key={u}>{u}</option>
          ))}
        </select>
        <select value={filter.vedouci} onChange={(e) => set({ vedouci: e.target.value })} aria-label="Nadřízený">
          <option value="">Všichni nadřízení</option>
          {vedouci.map((v) => (
            <option key={v.id} value={v.id}>
              Tým: {fullName(v)}
            </option>
          ))}
        </select>
        <select value={filter.kurz} onChange={(e) => set({ kurz: e.target.value })} aria-label="Školení">
          <option value="">Všechna školení</option>
          {ds.courses.filter((c) => c.povinnost.typ !== 'NEPOVINNE').map((c) => (
            <option key={c.kod} value={c.kod}>
              {c.nazev}
            </option>
          ))}
        </select>
        <select value={filter.stav} onChange={(e) => set({ stav: e.target.value as PeopleFilter['stav'] })} aria-label="Stav">
          <option value="problem">Jen s problémem (kromě platných)</option>
          <option value="prošlé">Prošlé</option>
          <option value="chybí">Chybí</option>
          <option value="brzy">Brzy vyprší</option>
          <option value="platné">Platné</option>
          <option value="vse">Všichni</option>
        </select>
        <button onClick={exportCsv}>Export CSV</button>
      </div>
      <p className="muted">{rows.length} lidí</p>
      <div className="tablewrap">
        <table>
          <thead>
            <tr>
              <th>Zaměstnanec</th>
              <th>Útvar / pozice</th>
              <th>Školení</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ e, rs, hit }) => {
              const g = groupOf(e, ref);
              const shown = filter.stav === 'vse' ? rs : hit;
              return (
                <Fragment key={e.id}>
                  <tr className="clickable" onClick={() => setOpen(open === e.id ? null : e.id)}>
                    <td>
                      <b>{fullName(e)}</b> <small className="muted">{e.id}</small>
                      {g !== 'aktivní' && <div><span className="tag">{GROUP_NOTE[g]}</span></div>}
                    </td>
                    <td>
                      {e.utvar}
                      {e.utvarOdvozen && <span className="tag" title="Útvar chyběl v datech a byl odvozen"> odvozeno</span>}
                      <div className="muted">{e.pozice}</div>
                    </td>
                    <td className="chips">
                      {shown.map((r) => (
                        <Badge key={r.course.kod} status={r.status}>
                          {r.course.kod}
                        </Badge>
                      ))}
                      {shown.length === 0 && <span className="muted">bez povinností</span>}
                    </td>
                  </tr>
                  {open === e.id && (
                    <tr className="detail">
                      <td colSpan={3}>
                        <div className="muted">
                          Nadřízený: {e.vedouciId ? fullName(empById.get(e.vedouciId)!) : 'neuveden / neznámý'} · Nástup: {fmtDate(e.nastup)}
                          {e.ukonceni && ` · Ukončení: ${fmtDate(e.ukonceni)}`}
                        </div>
                        <table className="inner">
                          <thead>
                            <tr>
                              <th>Školení</th>
                              <th>Stav</th>
                              <th>Naposledy</th>
                              <th>Platí do</th>
                              <th>Navržený termín</th>
                            </tr>
                          </thead>
                          <tbody>
                            {rs.map((r) => (
                              <tr key={r.course.kod}>
                                <td>{r.course.nazev}</td>
                                <td>
                                  <Badge status={r.status} />
                                  {r.daysLeft !== null && r.status !== 'platné' && <small className="muted"> {r.daysLeft < 0 ? `před ${-r.daysLeft} dny` : `za ${r.daysLeft} dní`}</small>}
                                </td>
                                <td>{fmtDate(r.lastDate)}</td>
                                <td>{r.course.platnostMesicu === 0 ? 'bez expirace' : fmtDate(r.validUntil)}</td>
                                <td>{planned.get(e.id + '|' + r.course.kod) ?? '–'}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

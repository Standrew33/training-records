import { useState } from 'react';
import type { Plan as PlanT } from '../lib/planner';
import { freeSeats } from '../lib/planner';
import { Badge, downloadCsv, fmtDate, fmtKc, fullName, STATUS_LABEL, type Ctx } from '../ui';

export default function Plan({ ctx, plan }: { ctx: Ctx; plan: PlanT }) {
  const { ds, empById, ref } = ctx;
  const [kurz, setKurz] = useState('');
  const courseName = new Map(ds.courses.map((c) => [c.kod, c.nazev]));
  const terms = ds.terms.filter((t) => t.vyrazen === null && t.datum >= ref && (!kurz || t.kod === kurz) && (plan.byTerm.get(t.id)?.length ?? 0) > 0);
  const mgr = (id: string) => {
    const e = empById.get(id);
    return e?.vedouciId ? fullName(empById.get(e.vedouciId)!) : '';
  };

  const exportCsv = () =>
    downloadCsv(
      'plan-prihlaseni.csv',
      ['Termín', 'Datum', 'Čas', 'Místo', 'Školení', 'Zaměstnanec', 'ID', 'Útvar', 'Nadřízený', 'Důvod', 'Platnost do', 'Pozn.'],
      plan.assignments
        .sort((a, b) => a.term.datum.localeCompare(b.term.datum) || a.req.emp.prijmeni.localeCompare(b.req.emp.prijmeni, 'cs'))
        .map((a) => [a.term.id, a.term.datum, a.term.cas, a.term.misto, courseName.get(a.term.kod) ?? a.term.kod, fullName(a.req.emp), a.req.emp.id, a.req.emp.utvar, mgr(a.req.emp.id), STATUS_LABEL[a.req.status], a.req.validUntil ?? '', a.pozde && a.req.status === 'brzy' ? 'termín je po expiraci' : '']),
    );

  return (
    <>
      <section className="card">
        <h2>Doporučené přihlášení na termíny</h2>
        <p className="muted">
          Postup: nejdřív ti, komu školení už vypršelo nebo chybí, pak ti, komu končí platnost nejdřív. Každému se vybere nejbližší termín s volným místem, který stihne
          před vypršením (nebo nejbližší možný), s ohledem na kapacitu (kapacita − už obsazeno) a bez kolizí. Zaměstnanci na rodičovské a ukončení se neplánují.
          Systém neví, kdo je už přihlášen na „obsazeno“ místech – před odesláním to ověřte.
        </p>
        <div className="kpis small">
          <div className="kpi">
            <div className="n">{plan.assignments.length}</div>navržených přihlášek
          </div>
          <div className="kpi">
            <div className="n">{fmtKc(plan.naklady)}</div>odhad nákladů
          </div>
          <div className={`kpi ${plan.unassigned.length ? 'warn' : ''}`}>
            <div className="n">{plan.unassigned.length}</div>bez termínu
          </div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Školení</th>
              <th>Potřebuje</th>
              <th>Volných míst</th>
              <th>Nepokryto</th>
            </tr>
          </thead>
          <tbody>
            {[...plan.poKurzu.entries()].map(([k, v]) => (
              <tr key={k}>
                <td>{courseName.get(k)}</td>
                <td>{v.potreba}</td>
                <td>{v.volno}</td>
                <td>{v.nepokryto ? <span className="badge bad">{v.nepokryto} – vypsat termín</span> : '0'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="filters">
          <select value={kurz} onChange={(e) => setKurz(e.target.value)} aria-label="Školení">
            <option value="">Všechna školení</option>
            {[...plan.poKurzu.keys()].map((k) => (
              <option key={k} value={k}>
                {courseName.get(k)}
              </option>
            ))}
          </select>
          <button onClick={exportCsv}>Export plánu (CSV)</button>
        </div>
      </section>

      {terms.map((t) => {
        const as = plan.byTerm.get(t.id)!;
        const free = freeSeats(t);
        return (
          <section key={t.id} className="card term">
            <h3>
              {courseName.get(t.kod)} · {fmtDate(t.datum)} {t.cas} · {t.misto}
              {t.vikend && <span className="tag warn"> víkend</span>}
            </h3>
            <p className="muted">
              {t.id} · kapacita {t.kapacita}, obsazeno {t.obsazeno}, volných {free} → navrženo {as.length}, zbývá {free - as.length}
              {t.cena !== null && ` · ${fmtKc(t.cena)}/os${t.cenaOdhad ? ' (odhad)' : ''}`}
            </p>
            <div className="meter">
              <div style={{ width: `${Math.min(100, ((t.obsazeno + as.length) / t.kapacita) * 100)}%` }} className="fill" />
              <div style={{ width: `${Math.min(100, (t.obsazeno / t.kapacita) * 100)}%` }} className="base" />
            </div>
            <ul className="people">
              {as.map((a) => (
                <li key={a.req.emp.id}>
                  <b>{fullName(a.req.emp)}</b> <small className="muted">{a.req.emp.utvar}</small> <Badge status={a.req.status} />
                  {a.req.validUntil && a.req.status !== 'chybí' && <small className="muted"> platnost {a.req.status === 'prošlé' ? 'skončila' : 'končí'} {fmtDate(a.req.validUntil)}</small>}
                  {a.pozde && a.req.status === 'brzy' && <span className="tag warn"> termín je až po expiraci</span>}
                </li>
              ))}
            </ul>
          </section>
        );
      })}

      {plan.unassigned.length > 0 && (
        <section className="card">
          <h2>Bez přiděleného termínu ({plan.unassigned.length})</h2>
          <ul className="people">
            {plan.unassigned.map((u) => (
              <li key={u.req.emp.id + u.req.course.kod}>
                <b>{fullName(u.req.emp)}</b> – {u.req.course.nazev} <Badge status={u.req.status} /> <small className="muted">{u.duvod}</small>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}

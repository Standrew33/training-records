import type { Plan } from '../lib/planner';
import { freeSeats } from '../lib/planner';
import { fmtDate, fmtKc, type Ctx } from '../ui';

export default function Terms({ ctx, plan }: { ctx: Ctx; plan: Plan }) {
  const { ds, ref } = ctx;
  const name = new Map(ds.courses.map((c) => [c.kod, c.nazev]));
  return (
    <section className="card">
      <h2>Vypsané termíny</h2>
      <div className="tablewrap">
        <table>
          <thead>
            <tr>
              <th>Termín</th>
              <th>Školení</th>
              <th>Datum a místo</th>
              <th>Kapacita</th>
              <th>Cena</th>
              <th>Poznámky</th>
            </tr>
          </thead>
          <tbody>
            {ds.terms.map((t) => {
              const added = plan.byTerm.get(t.id)?.length ?? 0;
              const past = t.datum && t.datum < ref;
              const notes = [t.vyrazen && `nepoužitelný: ${t.vyrazen}`, past && !t.vyrazen && 'proběhl před zvoleným datem', t.vikend && 'víkend', t.obsazeno > t.kapacita && 'přeplněno', t.cenaOdhad && 'cena odhadnuta'].filter(Boolean);
              return (
                <tr key={t.id} className={t.vyrazen || past ? 'dim' : ''}>
                  <td>{t.id}</td>
                  <td>{name.get(t.kod) ?? t.kod}</td>
                  <td>
                    {fmtDate(t.datum)} {t.cas}
                    <div className="muted">{t.misto}</div>
                  </td>
                  <td>
                    {t.obsazeno}/{t.kapacita}
                    {!t.vyrazen && !past && <div className="muted">volno {freeSeats(t)}{added ? `, plán +${added}` : ''}</div>}
                  </td>
                  <td>{t.cena !== null ? fmtKc(t.cena) : '–'}</td>
                  <td>{notes.map((n) => <span className="tag" key={String(n)}>{n}</span>)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

import { groupOf } from '../lib/compliance';
import type { Plan } from '../lib/planner';
import { freeSeats } from '../lib/planner';
import { StackBar, fmtDate, fmtKc, type Ctx } from '../ui';

export default function Overview({ ctx, plan }: { ctx: Ctx; plan: Plan }) {
  const { ds, reqs, ref, soon, nav } = ctx;
  const active = reqs.filter((r) => groupOf(r.emp, ref) !== 'rodičovská');
  const cnt = (s: string, rs = active) => rs.filter((r) => r.status === s).length;
  const okPct = active.length ? Math.round(((cnt('platné') + cnt('brzy')) / active.length) * 100) : 0;
  const peopleWithProblem = new Set(active.filter((r) => r.status !== 'platné').map((r) => r.emp.id)).size;
  const people = ds.employees.filter((e) => ['aktivní', 'nastupuje'].includes(groupOf(e, ref)));
  const onLeave = ds.employees.filter((e) => groupOf(e, ref) === 'rodičovská').length;
  const leaveProblems = reqs.filter((r) => groupOf(r.emp, ref) === 'rodičovská' && r.status !== 'platné' && r.status !== 'brzy').length;

  const usable = ds.terms.filter((t) => t.vyrazen === null && t.datum >= ref);
  const soonTerms = usable.filter((t) => {
    const added = plan.byTerm.get(t.id)?.length ?? 0;
    return t.datum <= addDays(ref, 21) && (freeSeats(t) - added <= 0 || freeSeats(t) === 0);
  });
  const shortage = [...plan.poKurzu.entries()].filter(([, v]) => v.nepokryto > 0);
  const lateSoon = plan.assignments.filter((a) => a.pozde && a.req.status === 'brzy');
  const unusable = ds.terms.filter((t) => t.vyrazen !== null);

  const byCourse = ds.courses
    .filter((c) => c.povinnost.typ !== 'NEPOVINNE')
    .map((c) => ({ c, rs: active.filter((r) => r.course.kod === c.kod) }))
    .filter((x) => x.rs.length);
  const depts = [...new Set(people.map((e) => e.utvar))].sort();

  return (
    <>
      <section className="kpis">
        <Kpi n={`${okPct} %`} label="povinných školení v pořádku" sub={`${active.length} povinností u ${people.length} lidí`} />
        <Kpi n={cnt('prošlé') + cnt('chybí')} label="prošlých nebo chybějících" sub={`${cnt('prošlé')} prošlých, ${cnt('chybí')} chybí`} tone="bad" onClick={() => nav('lide', { stav: 'problem' })} />
        <Kpi n={cnt('brzy')} label={`vyprší do ${soon} dní`} sub="do data " tone="soon" onClick={() => nav('lide', { stav: 'brzy' })} extra={fmtDate(addDays(ref, soon))} />
        <Kpi n={peopleWithProblem} label="lidí s alespoň jedním problémem" sub={`z ${people.length}`} onClick={() => nav('lide', { stav: 'problem' })} />
      </section>

      <section className="card">
        <h2>Co je potřeba řešit</h2>
        <ul className="todo">
          <li>
            <b>{cnt('prošlé') + cnt('chybí')}</b> povinných školení je prošlých nebo chybí – plán je přihlásí na nejbližší volné termíny.{' '}
            <button className="link" onClick={() => nav('plan')}>
              Otevřít plán ({plan.assignments.length} přihlášek, odhad {fmtKc(plan.naklady)})
            </button>
          </li>
          {lateSoon.length > 0 && (
            <li>
              <b>{lateSoon.length}</b> školení, kterému končí platnost, <b>nestihne</b> žádný volný termín před expirací.{' '}
              <button className="link" onClick={() => nav('plan')}>
                Zobrazit
              </button>
            </li>
          )}
          {shortage.length > 0 ? (
            shortage.map(([k, v]) => (
              <li key={k}>
                <b>{k}</b>: chybí {v.nepokryto} míst – nutné vypsat další termín.
              </li>
            ))
          ) : (
            <li>Kapacity vypsaných termínů pokryjí všechny aktuální potřeby (přes {plan.assignments.length} přihlášek).</li>
          )}
          {soonTerms.length > 0 && (
            <li>
              Termíny do 3 týdnů, které jsou plné nebo se zaplní plánem: {soonTerms.map((t) => `${t.kod} ${fmtDate(t.datum)}`).join(', ')}.
            </li>
          )}
          {onLeave > 0 && (
            <li>
              {onLeave} lidí je na rodičovské (u {leaveProblems} povinností by dnes bylo prošlé/chybí) – nezapočítáváno, řešit před návratem.
            </li>
          )}
          {unusable.length > 0 && (
            <li>
              {unusable.length} termínů ve vstupních datech je nepoužitelných ({unusable.map((t) => `${t.id}: ${t.vyrazen}`).join('; ')}).{' '}
              <button className="link" onClick={() => nav('kvalita')}>
                Kvalita dat
              </button>
            </li>
          )}
        </ul>
      </section>

      <section className="card">
        <h2>Stav podle školení</h2>
        <Legend />
        <div className="rows">
          {byCourse.map(({ c, rs }) => (
            <div key={c.kod} className="row">
              <div className="lbl">
                <button className="link" onClick={() => nav('lide', { kurz: c.kod, stav: 'problem' })}>
                  {c.nazev}
                </button>
                <small>{rs.length} povinných</small>
              </div>
              <StackBar parts={parts(rs)} />
              <div className="nums">{numText(rs)}</div>
            </div>
          ))}
        </div>
      </section>

      <section className="card">
        <h2>Stav podle útvarů</h2>
        <Legend />
        <div className="rows">
          {depts.map((d) => {
            const rs = active.filter((r) => r.emp.utvar === d);
            const ok = rs.filter((r) => r.status === 'platné' || r.status === 'brzy').length;
            return (
              <div key={d} className="row">
                <div className="lbl">
                  <button className="link" onClick={() => nav('lide', { utvar: d, stav: 'problem' })}>
                    {d}
                  </button>
                  <small>{people.filter((e) => e.utvar === d).length} lidí · {rs.length ? Math.round((ok / rs.length) * 100) : 100} % v pořádku</small>
                </div>
                <StackBar parts={parts(rs)} />
                <div className="nums">{numText(rs)}</div>
              </div>
            );
          })}
        </div>
      </section>
    </>
  );
}

const parts = (rs: { status: string }[]) => [
  { cls: 'ok', n: rs.filter((r) => r.status === 'platné').length, label: 'Platné' },
  { cls: 'soon', n: rs.filter((r) => r.status === 'brzy').length, label: 'Brzy vyprší' },
  { cls: 'bad', n: rs.filter((r) => r.status === 'prošlé').length, label: 'Prošlé' },
  { cls: 'miss', n: rs.filter((r) => r.status === 'chybí').length, label: 'Chybí' },
];
const numText = (rs: { status: string }[]) => {
  const p = parts(rs);
  return `${p[0].n} / ${p[1].n} / ${p[2].n} / ${p[3].n}`;
};

function Legend() {
  return (
    <p className="legend">
      <span className="badge ok">Platné</span> <span className="badge soon">Brzy vyprší</span> <span className="badge bad">Prošlé</span>{' '}
      <span className="badge miss">Chybí</span> <small>čísla vpravo: v tomto pořadí</small>
    </p>
  );
}

function Kpi(p: { n: string | number; label: string; sub?: string; extra?: string; tone?: string; onClick?: () => void }) {
  const body = (
    <>
      <div className={`n ${p.tone ?? ''}`}>{p.n}</div>
      <div>{p.label}</div>
      <small>
        {p.sub}
        {p.extra}
      </small>
    </>
  );
  return p.onClick ? (
    <button className="kpi click" onClick={p.onClick}>
      {body}
    </button>
  ) : (
    <div className="kpi">{body}</div>
  );
}

function addDays(iso: string, n: number) {
  return new Date(Date.parse(iso) + n * 86400000).toISOString().slice(0, 10);
}

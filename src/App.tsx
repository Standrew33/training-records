import { useEffect, useMemo, useState } from 'react';
import type { Dataset } from './lib/types';
import { computeRequirements } from './lib/compliance';
import { buildPlan } from './lib/planner';
import { emptyFilter, type Ctx, type PeopleFilter, type Tab } from './ui';
import Overview from './views/Overview';
import People from './views/People';
import Plan from './views/Plan';
import Terms from './views/Terms';
import Quality from './views/Quality';

const TABS: [Tab, string][] = [
  ['prehled', 'Přehled'],
  ['lide', 'Lidé'],
  ['plan', 'Plán přihlášení'],
  ['terminy', 'Termíny'],
  ['kvalita', 'Kvalita dat'],
];

export default function App() {
  const [ds, setDs] = useState<Dataset | null>(null);
  const [err, setErr] = useState('');
  const [tab, setTab] = useState<Tab>('prehled');
  const [ref, setRef] = useState('');
  const [soon, setSoon] = useState(90);
  const [filter, setFilter] = useState<PeopleFilter>(emptyFilter);

  useEffect(() => {
    fetch(`${import.meta.env.BASE_URL}data/dataset.json`)
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((d: Dataset) => {
        setDs(d);
        setRef(d.exportDatum);
      })
      .catch((e) => setErr(String(e)));
  }, []);

  const reqs = useMemo(() => (ds && ref ? computeRequirements(ds, ref, soon) : []), [ds, ref, soon]);
  const plan = useMemo(() => (ds && ref ? buildPlan(ds, reqs, ref) : null), [ds, reqs, ref]);

  if (err) return <p className="msg">Nepodařilo se načíst data: {err}</p>;
  if (!ds || !plan || !ref) return <p className="msg">Načítám…</p>;

  const ctx: Ctx = {
    ds,
    ref,
    soon,
    reqs,
    empById: new Map(ds.employees.map((e) => [e.id, e])),
    nav: (t, f) => {
      if (f) setFilter({ ...emptyFilter, ...f });
      setTab(t);
      window.scrollTo(0, 0);
    },
  };

  return (
    <>
      <header className="top">
        <div className="top-in">
          <h1>Evidence školení</h1>
          <div className="controls">
            <label>
              Stav k datu
              <input type="date" value={ref} onChange={(e) => e.target.value && setRef(e.target.value)} />
            </label>
            <label>
              „Brzy vyprší“ = do
              <select value={soon} onChange={(e) => setSoon(+e.target.value)}>
                {[30, 60, 90, 180].map((n) => (
                  <option key={n} value={n}>
                    {n} dní
                  </option>
                ))}
              </select>
            </label>
            {ref !== ds.exportDatum && (
              <button className="link" onClick={() => setRef(ds.exportDatum)}>
                zpět na datum exportu
              </button>
            )}
          </div>
        </div>
        <nav className="tabs" aria-label="Sekce">
          {TABS.map(([k, label]) => (
            <button key={k} className={tab === k ? 'on' : ''} aria-current={tab === k ? 'page' : undefined} onClick={() => setTab(k)}>
              {label}
            </button>
          ))}
        </nav>
      </header>
      <main>
        {tab === 'prehled' && <Overview ctx={ctx} plan={plan} />}
        {tab === 'lide' && <People ctx={ctx} plan={plan} filter={filter} setFilter={setFilter} />}
        {tab === 'plan' && <Plan ctx={ctx} plan={plan} />}
        {tab === 'terminy' && <Terms ctx={ctx} plan={plan} />}
        {tab === 'kvalita' && <Quality ctx={ctx} />}
      </main>
      <footer>
        Fiktivní data, export z {ds.exportDatum.split('-').reverse().map(Number).join('. ')}. Rodná čísla ani poznámky z exportů se do aplikace nepřenášejí.
      </footer>
    </>
  );
}

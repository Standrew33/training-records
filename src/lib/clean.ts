import Papa from 'papaparse';
import type { Completion, Course, Dataset, Employee, Issue, Povinnost, Term } from './types';
import { addMonths, editDistance, isWeekend, normKey, parseDate, parseMoney, titleCase } from './parse';

export interface RawInputs {
  zamestnanci: string;
  absolvovana: string;
  katalog: string;
  terminy: string;
}

const csv = (text: string, delimiter?: string) =>
  Papa.parse<Record<string, string>>(text.replace(/^﻿/, ''), {
    header: true,
    skipEmptyLines: 'greedy',
    delimiter,
    transformHeader: (h) => h.trim(),
  }).data;

const t = (v: string | undefined) => (v ?? '').trim();

export function buildDataset(raw: RawInputs, exportDatum: string): Dataset {
  const issues: Issue[] = [];
  const add = (i: Issue) => issues.push(i);

  /* ---------- katalog ---------- */
  const courses: Course[] = csv(raw.katalog)
    .filter((r) => t(r.kod))
    .map((r) => {
      const kod = t(r.kod);
      const pm = t(r.platnost_mesicu).toLowerCase();
      let platnost: number | null = null;
      const yrs = pm.match(/^(\d+)\s*(rok|roky|let)$/);
      if (yrs) {
        platnost = +yrs[1] * 12;
        add({ zdroj: 'katalog', severity: 'info', ref: kod, popis: `Platnost zapsána slovy („${pm}“)`, rizeni: `Převedeno na ${platnost} měsíců.` });
      } else if (/^\d+$/.test(pm)) platnost = +pm;
      const pf = t(r.povinne_pro);
      let povinnost: Povinnost = { typ: 'NEPOVINNE' };
      if (pf === 'VSICHNI') povinnost = { typ: 'VSICHNI' };
      else if (pf.startsWith('UTVAR:')) povinnost = { typ: 'UTVAR', hodnoty: pf.slice(6).split(';').map((x) => x.trim()) };
      else if (pf.startsWith('POZICE:')) povinnost = { typ: 'POZICE', hodnoty: pf.slice(7).split(';').map((x) => x.trim()) };
      if (platnost === 0)
        add({ zdroj: 'katalog', severity: 'info', ref: kod, popis: 'Platnost 0 měsíců', rizeni: 'Chápáno jako „jednorázové, bez expirace“.' });
      return { kod, nazev: t(r.nazev), platnostMesicu: platnost, povinnost };
    });
  const courseCodes = courses.map((c) => c.kod);

  /** Opraví kód školení (mezery, velikost, _ místo -, překlep o 1 znak jen při jednoznačné shodě). */
  const fixCode = (rawCode: string): { kod: string | null; opraveno: boolean } => {
    const c = rawCode.trim().toUpperCase().replace(/[_\s]+/g, '-');
    if (courseCodes.includes(c)) return { kod: c, opraveno: c !== rawCode };
    // tolerantní jen k záměně písmene za číslici (KYB3R); „BOZP-X“ by bylo hádání, to raději hlásíme jako chybu
    const near = /\d/.test(c) ? courseCodes.filter((k) => !/\d/.test(k) && editDistance(k, c) <= 1) : [];
    return near.length === 1 ? { kod: near[0], opraveno: true } : { kod: null, opraveno: false };
  };

  /* ---------- zaměstnanci ---------- */
  const rawEmp = csv(raw.zamestnanci);
  const deptVariants = new Map<string, Map<string, number>>();
  for (const r of rawEmp) {
    const d = t(r.utvar);
    if (!d) continue;
    const m = deptVariants.get(normKey(d)) ?? new Map<string, number>();
    m.set(d, (m.get(d) ?? 0) + 1);
    deptVariants.set(normKey(d), m);
  }
  const deptCanon = new Map<string, string>();
  for (const [k, m] of deptVariants) deptCanon.set(k, [...m.entries()].sort((a, b) => b[1] - a[1])[0][0]);
  // zkrácený název („Studijní“) -> jediný delší útvar, který jím začíná („Studijní oddělení“)
  for (const k of [...deptCanon.keys()]) {
    const longer = [...deptCanon.keys()].filter((o) => o !== k && o.startsWith(k + ' '));
    if (longer.length === 1) deptCanon.set(k, deptCanon.get(longer[0])!);
  }

  // duplicity zaměstnanců: shodné rodné číslo (jen v době buildu, do výstupu nejde)
  const byRc = new Map<string, string[]>();
  for (const r of rawEmp) {
    const rc = t(r.rodne_cislo);
    if (rc) byRc.set(rc, [...(byRc.get(rc) ?? []), t(r.id_zamestnance)]);
  }
  const alias = new Map<string, string>();
  for (const ids of byRc.values()) {
    if (ids.length < 2) continue;
    const keep = [...ids].sort()[0];
    for (const id of ids)
      if (id !== keep) {
        alias.set(id, keep);
        add({ zdroj: 'zaměstnanci', severity: 'chyba', ref: id, popis: `Duplicitní osoba (shodné rodné číslo jako ${keep}, jméno s překlepem/bez diakritiky)`, rizeni: `Sloučeno do ${keep}; záznamy školení přesměrovány.` });
      }
  }

  const emailOk = (e: string) => /^[^\s@,]+@[^\s@,]+\.[a-z]{2,}$/i.test(e);
  const employees: Employee[] = [];
  for (const r of rawEmp) {
    const id = t(r.id_zamestnance);
    if (alias.has(id)) continue;
    const rawEmail = t(r.email);
    const email = rawEmail.replace(/\s+/g, '').toLowerCase();
    if (rawEmail !== email || !emailOk(email))
      add({ zdroj: 'zaměstnanci', severity: 'varování', ref: id, popis: `E-mail „${rawEmail}“ ${emailOk(email) ? 'obsahuje mezeru' : 'je neplatný'}`, rizeni: emailOk(email) ? 'Mezery odstraněny.' : 'Ponecháno beze změny; k párování se nepoužívá.' });
    const jmeno = titleCase(t(r.jmeno)),
      prijmeni = titleCase(t(r.prijmeni));
    if (jmeno !== r.jmeno || prijmeni !== r.prijmeni)
      add({ zdroj: 'zaměstnanci', severity: 'info', ref: id, popis: `Jméno „${r.jmeno}“ / „${r.prijmeni}“ s nekonzistentní velikostí písmen či mezerami`, rizeni: 'Normalizováno.' });
    const nastupRaw = t(r.datum_nastupu);
    const nastup = parseDate(nastupRaw);
    if (nastup && !/^\d{4}-/.test(nastupRaw)) add({ zdroj: 'zaměstnanci', severity: 'info', ref: id, popis: `Datum nástupu v jiném formátu („${nastupRaw}“)`, rizeni: 'Převedeno na ISO.' });
    const ukonceni = parseDate(t(r.datum_ukonceni));
    const stavRaw = normKey(t(r.stav));
    const stav: Employee['stav'] = stavRaw.startsWith('ukon') ? 'ukončen' : stavRaw.startsWith('rodic') ? 'rodičovská' : 'aktivní';
    if (nastup && nastup > exportDatum) add({ zdroj: 'zaměstnanci', severity: 'info', ref: id, popis: `Nástup až ${nastup} (po exportu)`, rizeni: 'Označen jako nastupující; školení se mu plánují před nástupem.' });
    if (stav === 'aktivní' && ukonceni && ukonceni > exportDatum) add({ zdroj: 'zaměstnanci', severity: 'info', ref: id, popis: `Aktivní, ale s plánovaným ukončením ${ukonceni}`, rizeni: 'Plánování ho nepřihlásí na termín po datu ukončení.' });
    let utvar = '';
    const d = t(r.utvar);
    if (d) {
      utvar = deptCanon.get(normKey(d)) ?? d;
      if (utvar !== d) add({ zdroj: 'zaměstnanci', severity: 'info', ref: id, popis: `Útvar „${d}“ zapsán jinak než jinde`, rizeni: `Sjednoceno na „${utvar}“.` });
    }
    employees.push({ id, jmeno, prijmeni, email, utvar, utvarOdvozen: false, pozice: t(r.pozice), vedouciEmail: t(r.vedouci_email).toLowerCase(), vedouciId: null, nastup, ukonceni, stav });
  }
  const byEmail = new Map(employees.map((e) => [e.email, e]));
  for (const e of employees) {
    if (!e.vedouciEmail) continue;
    const m = byEmail.get(e.vedouciEmail);
    if (m) e.vedouciId = m.id;
    else add({ zdroj: 'zaměstnanci', severity: 'varování', ref: e.id, popis: `Nadřízený „${e.vedouciEmail}“ v evidenci neexistuje`, rizeni: 'Bez nadřízeného; zaměstnanec je vidět jen v celkovém přehledu.' });
  }
  // chybějící útvar: z podřízených (většina), jinak z nadřízeného
  for (const e of employees.filter((x) => !x.utvar)) {
    const subs = employees.filter((x) => x.vedouciId === e.id && x.utvar);
    const cnt = new Map<string, number>();
    subs.forEach((s) => cnt.set(s.utvar, (cnt.get(s.utvar) ?? 0) + 1));
    const fromSubs = [...cnt.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
    const fromMgr = employees.find((x) => x.id === e.vedouciId)?.utvar;
    e.utvar = fromSubs ?? fromMgr ?? 'Neuvedeno';
    e.utvarOdvozen = true;
    add({ zdroj: 'zaměstnanci', severity: 'varování', ref: e.id, popis: 'Chybí útvar', rizeni: `Odvozeno: „${e.utvar}“ (${fromSubs ? 'z podřízených' : fromMgr ? 'z nadřízeného' : 'nelze odvodit'}). Ověřit.` });
  }

  /* ---------- absolvovaná školení ---------- */
  const empIds = new Set(employees.map((e) => e.id));
  const completions: Completion[] = [];
  const seenId = new Set<string>();
  const seenKey = new Set<string>();
  const courseByCode = new Map(courses.map((c) => [c.kod, c]));
  for (const r of csv(raw.absolvovana, ';')) {
    const id = t(r.id_zaznamu);
    if (!/^Z\d+$/.test(id)) {
      if (id && id !== 'id_zaznamu') add({ zdroj: 'absolvovaná', severity: 'info', ref: id.slice(0, 40), popis: 'Řádek, který není záznam školení (komentář / součet / opakovaná hlavička)', rizeni: 'Ignorován. Součet „Celkem“ ani poznámka „nezapočítávat“ se do statistik nezapočítávají.' });
      continue;
    }
    const code = fixCode(r.kod_skoleni ?? '');
    if (!code.kod) {
      add({ zdroj: 'absolvovaná', severity: 'chyba', ref: id, popis: `Neznámý kód školení „${r.kod_skoleni}“`, rizeni: 'Záznam vyřazen (nelze jednoznačně přiřadit).' });
      continue;
    }
    if (code.opraveno) add({ zdroj: 'absolvovaná', severity: 'info', ref: id, popis: `Kód „${r.kod_skoleni}“ zapsán nestandardně`, rizeni: `Opraveno na ${code.kod}.` });
    const rawEmpId = t(r.id_zamestnance);
    const empId = alias.get(rawEmpId) ?? rawEmpId;
    if (!empIds.has(empId)) {
      add({ zdroj: 'absolvovaná', severity: 'chyba', ref: id, popis: `Záznam odkazuje na neexistujícího zaměstnance ${rawEmpId}`, rizeni: 'Vyřazen (osiřelý záznam).' });
      continue;
    }
    if (alias.has(rawEmpId)) add({ zdroj: 'absolvovaná', severity: 'info', ref: id, popis: `Záznam vedený na duplicitní ID ${rawEmpId}`, rizeni: `Přesunut na ${empId}.` });
    const dRaw = t(r.datum_absolvovani);
    const datum = parseDate(dRaw);
    if (!datum || datum < '2000-01-01') {
      add({ zdroj: 'absolvovaná', severity: 'chyba', ref: id, popis: `Neplatné datum absolvování „${dRaw}“ (zástupná hodnota 1900)`, rizeni: 'Záznam vyřazen; školení se tím pádem považuje za nedoložené.' });
      continue;
    }
    if (/^\d{5}$/.test(dRaw)) add({ zdroj: 'absolvovaná', severity: 'info', ref: id, popis: `Datum jako číslo z Excelu (${dRaw})`, rizeni: `Převedeno na ${datum}.` });
    if (datum > exportDatum) {
      add({ zdroj: 'absolvovaná', severity: 'varování', ref: id, popis: `Datum absolvování ${datum} je po exportu (${exportDatum}) – poznámka „${t(r.poznamka)}“`, rizeni: 'Považováno za předběžný zápis, do platnosti se nezapočítává.' });
      continue;
    }
    if (seenId.has(id)) {
      add({ zdroj: 'absolvovaná', severity: 'info', ref: id, popis: 'Duplicitní ID záznamu (stejný obsah, jiný formát data)', rizeni: 'Druhý výskyt vynechán.' });
      continue;
    }
    seenId.add(id);
    const key = `${empId}|${code.kod}|${datum}`;
    if (seenKey.has(key)) {
      add({ zdroj: 'absolvovaná', severity: 'info', ref: id, popis: 'Stejné školení stejného člověka ve stejný den zapsané dvakrát', rizeni: 'Vynecháno.' });
      continue;
    }
    seenKey.add(key);
    const platnostDo = parseDate(t(r.platnost_do));
    const pm = courseByCode.get(code.kod)!.platnostMesicu;
    if (platnostDo && pm) {
      const calc = addMonths(datum, pm);
      if (platnostDo !== calc) add({ zdroj: 'absolvovaná', severity: 'varování', ref: id, popis: `platnost_do ${platnostDo} nesedí s katalogem (${pm} měs. => ${calc})`, rizeni: 'Použita hodnota z katalogu.' });
    }
    let cena = parseMoney(r.cena_kc);
    if (cena !== null && cena < 0) {
      add({ zdroj: 'absolvovaná', severity: 'varování', ref: id, popis: `Záporná cena ${cena}`, rizeni: 'Cena ignorována.' });
      cena = null;
    }
    completions.push({ id, empId, kod: code.kod, datum, platnostDoZdroj: platnostDo, cena, poznamka: t(r.poznamka).slice(0, 200) });
  }

  /* ---------- termíny ---------- */
  const priceByCode = new Map<string, number[]>();
  const terms: Term[] = [];
  const seenTerm = new Map<string, string>();
  for (const r of csv(raw.terminy, ';')) {
    const id = t(r.id_terminu);
    const code = fixCode(r.kod_skoleni ?? '');
    if (code.opraveno && code.kod) add({ zdroj: 'termíny', severity: 'varování', ref: id, popis: `Kód „${r.kod_skoleni}“ v termínu`, rizeni: `Opraveno na ${code.kod}.` });
    const kod = code.kod ?? t(r.kod_skoleni);
    const dRaw = t(r.datum);
    const datum = parseDate(dRaw);
    const kapacita = Number(t(r.kapacita)) || 0;
    const obsazeno = Number(t(r.obsazeno)) || 0;
    const cena = parseMoney(r.cena_na_osobu);
    let vyrazen: string | null = null;
    if (!code.kod) vyrazen = 'neznámý kód školení';
    else if (!datum) vyrazen = `neplatné datum „${dRaw}“`;
    else if (normKey(t(r.stav)).startsWith('zrus')) vyrazen = 'zrušeno';
    else if (kapacita <= 0) vyrazen = 'nulová kapacita';
    if (!datum) add({ zdroj: 'termíny', severity: 'chyba', ref: id, popis: `Neplatné datum termínu „${dRaw}“`, rizeni: 'Termín vyřazen z plánování.' });
    if (kapacita <= 0) add({ zdroj: 'termíny', severity: 'chyba', ref: id, popis: 'Kapacita 0', rizeni: 'Termín vyřazen z plánování.' });
    if (obsazeno > kapacita && kapacita > 0) add({ zdroj: 'termíny', severity: 'varování', ref: id, popis: `Přeplněno: obsazeno ${obsazeno} z ${kapacita}`, rizeni: 'Volná kapacita = 0.' });
    if (cena === null) add({ zdroj: 'termíny', severity: 'varování', ref: id, popis: `Cena neuvedena („${t(r.cena_na_osobu)}“)`, rizeni: 'Pro odhad nákladů použita typická cena téhož školení.' });
    else if (!/^\d+$/.test(t(r.cena_na_osobu))) add({ zdroj: 'termíny', severity: 'info', ref: id, popis: `Cena zapsána jako „${t(r.cena_na_osobu)}“`, rizeni: `Převedeno na ${cena}.` });
    if (vyrazen === null && datum) {
      const dk = `${kod}|${datum}|${t(r.cas)}|${t(r.misto)}`;
      if (seenTerm.has(dk)) {
        vyrazen = `duplicita termínu ${seenTerm.get(dk)}`;
        add({ zdroj: 'termíny', severity: 'chyba', ref: id, popis: `Duplicita termínu ${seenTerm.get(dk)}`, rizeni: 'Vyřazen, aby se kapacita nepočítala dvakrát.' });
      } else seenTerm.set(dk, id);
    }
    const vikend = !!datum && isWeekend(datum);
    if (vikend) add({ zdroj: 'termíny', severity: 'varování', ref: id, popis: `Termín ${datum} připadá na víkend`, rizeni: 'Ponechán, v plánu označen. Ověřit se školitelem.' });
    if (cena !== null && vyrazen === null) priceByCode.set(kod, [...(priceByCode.get(kod) ?? []), cena]);
    terms.push({ id, kod, datum: datum ?? '', cas: t(r.cas), misto: t(r.misto), kapacita, obsazeno, cena, cenaOdhad: false, stav: t(r.stav), vyrazen, vikend });
  }
  for (const tm of terms)
    if (tm.cena === null) {
      const ps = (priceByCode.get(tm.kod) ?? []).sort((a, b) => a - b);
      if (ps.length) {
        tm.cena = ps[Math.floor(ps.length / 2)];
        tm.cenaOdhad = true;
      }
    }

  return { exportDatum, employees, courses, completions, terms: terms.sort((a, b) => a.datum.localeCompare(b.datum)), issues };
}
